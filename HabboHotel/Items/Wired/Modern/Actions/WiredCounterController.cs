using System.Globalization;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public enum WiredClockOrigin
{
    Unknown, ModernWired, Gui
}
public enum WiredClockReason
{
    Start, Stop, Reset, Pause, Resume, Adjust, Expiry, GameEnd, Detach
}
public sealed record WiredClockTransition(Item Item, object Clock, long Sequence, long Placement, ItemDefinition Definition,
    WiredClockOrigin Origin, WiredClockReason Reason, bool WasRunning, bool WasStarted, RoomUser? Actor, WiredClockReason? PreviousReason)
{
    internal bool OtherFootballClockRunning { get; init; }
    internal int X { get; } = Item.GetX;
    internal int Y { get; } = Item.GetY;
    internal double Z { get; } = Item.GetZ;
    internal int Rotation { get; } = Item.Rotation;
    internal uint ItemId { get; } = Item.Id;
    internal uint RoomId { get; } = Item.RoomId;
    internal uint OwnerId { get; } = Item.OwnerId;
    internal uint BaseItem { get; } = Definition.Id;
    internal string DefinitionName { get; } = Definition.ItemName;
}

public sealed record WiredCounterChange(Item Item, WiredRuntimeEvent Event, bool DisplayChanged);

/// <summary>Room-owned clock state. The room drains changes and publishes packets/events once.</summary>
public sealed class WiredCounterController(int maxHalfSeconds = 11999, Action<Item, bool>? gameControl = null, int defaultGameSeconds = 30,
    Action<WiredClockTransition>? observeTransition = null, Action<WiredClockTransition>? gameTransition = null,
    Func<Item, WiredClockOrigin, RoomUser?, bool>? canBegin = null)
{
    private sealed class Clock(Item item, bool game, bool countdown, long value)
    {
        public Item Item { get; } = item;
        public bool GameAware { get; } = game;
        public bool Countdown { get; } = countdown;
        public long Value = value; // Half seconds: elapsed for Wired counters, remaining for native game timers.
        public bool Running;
        public bool Started = !countdown && value > 0;
        public long NextTick;
        public long Sequence;
        public WiredClockReason? LastReason;
    }
    private readonly Dictionary<uint, Clock> _clocks = [];
    private readonly Queue<WiredCounterChange> _changes = [];
    internal bool HasPendingChanges => _changes.Count > 0;
    public bool HasRunning => _clocks.Values.Any(clock => clock.Running);
    public static bool Recognizes(Item item) => Name(item) is "wf_upcounter1" or "wf_upcounter2" or "wf_game_upcounter1" or "wf_game_upcounter2"
        || IsGameTimer(item);
    public static bool IsGameTimer(Item item) => item.Definition.ItemName is "bb_counter" or "fball_counter" or "es_counter"
        || Name(item) == "game_timer" || item.Definition.InteractionType is InteractionType.Banzaicounter or InteractionType.Freezetimer
        || item.Definition.InteractionType == InteractionType.Counter && !Name(item).StartsWith("wf_", StringComparison.Ordinal);
    private static string Name(Item item) => string.IsNullOrEmpty(item.Definition.InteractionName) ? item.Definition.ItemName : item.Definition.InteractionName;
    public bool Attach(Item item)
    {
        if (!Recognizes(item)) {
            return false;
        }

        var countdown = IsGameTimer(item);
        var game = countdown || Name(item).StartsWith("wf_game_", StringComparison.Ordinal);
        var seconds = int.TryParse(item.LegacyDataString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var saved) ? Math.Max(0, saved) : 0;
        _clocks[item.Id] = new(item, game, countdown, countdown ? seconds * 2L : Math.Min(maxHalfSeconds, seconds * 2L));

        return true;
    }
    public long? ReadMilliseconds(Item item) => Find(item) is { } clock ? clock.Value * 500L : null;
    public bool IsRunning(Item item) => Find(item)?.Running == true;
    public int? ReadState(Item item) => Find(item) is { } clock ? clock.Running ? 1 : clock.Started ? 2 : 0 : null;
    public long? ReadPulseCount(Item item) => Find(item)?.Value;
    public bool? ReadGameAware(Item item) => Find(item)?.GameAware;
    public bool SetPulseCount(Item item, int value)
    {
        var clock = Find(item);

        if (clock == null) {
            return false;
        }

        Evidence(clock, WiredClockReason.Adjust);
        var previous = clock.Value;
        clock.Value = Math.Clamp(clock.Countdown ? value / 2 * 2L : value, 0,
            clock.Countdown ? int.MaxValue * 2L : maxHalfSeconds);
        PublishClock(clock, previous);

        return true;
    }
    public void Forget(Item item)
    {
        if (Find(item) is { } clock) {
            Evidence(clock, WiredClockReason.Detach);
            _clocks.Remove(item.Id);
        }
    }
    public void Clear()
    {
        _clocks.Clear();
        _changes.Clear();
    }
    private Clock? Find(Item item) => _clocks.TryGetValue(item.Id, out var clock) && ReferenceEquals(clock.Item, item) ? clock : null;

    // Raw Octane clock control: 0 start, 1 stop, 2 reset, 3 pause, 4 resume.
    public bool Control(Item item, int operation, long nowMilliseconds, WiredClockOrigin origin = WiredClockOrigin.Unknown, RoomUser? actor = null)
    {
        var clock = Find(item);

        if (clock == null || operation is < 0 or > 4) {
            return false;
        }

        if (operation == 0 && !clock.Running && canBegin?.Invoke(item, origin, actor) == false) {
            return false;
        }

        if (operation == 2) {
            Stop(clock, WiredClockReason.Reset, origin, actor);

            if (!clock.Running) {
                Evidence(clock, WiredClockReason.Reset, origin, actor);
            }

            clock.Started = false;
            var previous = clock.Value;
            clock.Value = clock.Countdown ? defaultGameSeconds * 2L : 0;
            PublishClock(clock, previous);
        }

        if (operation is 0 or 4 && !clock.Running) {
            if (clock.Countdown && clock.Value <= 0) {
                var previous = clock.Value;
                clock.Value = defaultGameSeconds * 2L;
                PublishClock(clock, previous);
            }

            var transition = Evidence(clock, operation == 0 ? WiredClockReason.Start : WiredClockReason.Resume, origin, actor);
            clock.Running = true;
            clock.Started = true;
            clock.NextTick = nowMilliseconds + (clock.Countdown ? 1000 : 500);

            if (clock.Countdown) {
                if (gameTransition != null) {
                    gameTransition(transition);
                }
                else {
                    gameControl?.Invoke(item, true);
                }
            }
        }

        if (operation is 1 or 3) {
            Stop(clock, operation == 1 ? WiredClockReason.Stop : WiredClockReason.Pause, origin, actor);
        }

        return true;
    }
    // Octane and Turbo operators: 0 increase, 1 decrease, 2 set.
    public bool Adjust(Item item, int operation, int minutes, int halfSecondSteps)
    {
        var clock = Find(item);

        if (clock == null || operation is < 0 or > 2 || minutes is < 0 or > 99 || halfSecondSteps is < 0 or > 119) {
            return false;
        }

        var amount = minutes * 120L + halfSecondSteps;

        if (clock.Countdown) {
            amount = amount / 2 * 2;
        }

        Evidence(clock, WiredClockReason.Adjust);
        var previous = clock.Value;
        clock.Value = Math.Clamp(operation switch { 0 => previous + amount, 1 => previous - amount, _ => amount }, 0,
            clock.Countdown ? int.MaxValue * 2L : maxHalfSeconds);
        PublishClock(clock, previous);

        return true;
    }
    public bool Use(Item item, int parameter, long nowMilliseconds)
    {
        var clock = Find(item);

        if (clock == null) {
            return false;
        }

        return Control(item, parameter == 2 ? 2 : clock.Running ? 1 : 0, nowMilliseconds, WiredClockOrigin.Gui);
    }
    public void OnGameEnded()
    {
        foreach (var clock in _clocks.Values) {
            if (clock.GameAware && clock.Running) {
                Evidence(clock, WiredClockReason.GameEnd);
                clock.Running = false;
            }
        }
    }
    public IReadOnlyList<WiredCounterChange> Poll(long nowMilliseconds)
    {
        foreach (var clock in _clocks.Values) {
            if (!clock.Running || nowMilliseconds < clock.NextTick) {
                continue;
            }

            if (clock.Countdown ? clock.Value <= 0 : clock.Value >= maxHalfSeconds) {
                Stop(clock);
                continue;
            }

            var previous = clock.Value;
            clock.Value = clock.Countdown ? Math.Max(0, clock.Value - 2) : clock.Value + 1;
            clock.NextTick = nowMilliseconds + (clock.Countdown ? 1000 : 500);
            PublishClock(clock, previous);

            if (clock.Countdown && clock.Value == 0) {
                Stop(clock);
            }
        }

        return TakeChanges();
    }
    public IReadOnlyList<WiredCounterChange> TakeChanges()
    {
        var result = new List<WiredCounterChange>();

        while (_changes.TryDequeue(out var change)) {
            if (Find(change.Item) != null) {
                result.Add(change);
            }
        }

        return result;
    }
    private void Stop(Clock clock, WiredClockReason reason = WiredClockReason.Expiry, WiredClockOrigin origin = WiredClockOrigin.Unknown, RoomUser? actor = null)
    {
        if (!clock.Running) {
            return;
        }

        var transition = Capture(clock, reason, origin, actor);
        clock.Running = false;
        observeTransition?.Invoke(transition);

        if (clock.Countdown) {
            if (gameTransition != null) {
                gameTransition(transition);
            }
            else {
                gameControl?.Invoke(clock.Item, false);
            }
        }
    }
    private WiredClockTransition Evidence(Clock clock, WiredClockReason reason, WiredClockOrigin origin = WiredClockOrigin.Unknown, RoomUser? actor = null)
    {
        var transition = Capture(clock, reason, origin, actor);
        observeTransition?.Invoke(transition);

        return transition;
    }
    private WiredClockTransition Capture(Clock clock, WiredClockReason reason, WiredClockOrigin origin, RoomUser? actor)
    {
        var transition = new WiredClockTransition(clock.Item, clock, ++clock.Sequence, clock.Item.Placement, clock.Item.Definition,
            origin, reason, clock.Running, clock.Started, actor, clock.LastReason)
        {
            OtherFootballClockRunning = _clocks.Values.Any(other => !ReferenceEquals(other, clock) && other.Running
                && other.Item.Definition.ItemName == "fball_counter")
        };
        clock.LastReason = reason;

        return transition;
    }
    private void PublishClock(Clock clock, long previous) => PublishDisplay(clock,
        new(WiredEventKind.Counter) { EventItem = clock.Item, PreviousValue = previous * 500L, Value = clock.Value * 500L });
    private void PublishDisplay(Clock clock, WiredRuntimeEvent @event)
    {
        var text = (clock.Value / 2).ToString(CultureInfo.InvariantCulture);
        var changed = clock.Item.LegacyDataString != text;

        if (changed) {
            clock.Item.LegacyDataString = text;
        }

        _changes.Enqueue(new(clock.Item, @event, changed));
    }
}
