using System.Globalization;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public sealed record WiredCounterChange(Item Item, WiredRuntimeEvent Event, bool DisplayChanged);

/// <summary>Room-owned clock state. The room drains changes and publishes packets/events once.</summary>
public sealed class WiredCounterController(int maxHalfSeconds = 11999, int defaultGameSeconds = 60)
{
    private sealed class Clock(Item item, bool game, int value)
    {
        public Item Item { get; } = item;
        public bool Game { get; } = game;
        public int Value = value; // Half seconds for clock counters, whole seconds for game countdowns.
        public bool Running;
        public long NextTick;
    }
    private readonly Dictionary<uint, Clock> _clocks = [];
    private readonly Queue<WiredCounterChange> _changes = [];
    public bool HasRunning => _clocks.Values.Any(clock => clock.Running);
    public static bool Recognizes(Item item) => Name(item) is "wf_upcounter1" or "wf_upcounter2" or "wf_game_upcounter1" or "wf_game_upcounter2";
    private static string Name(Item item) => string.IsNullOrEmpty(item.Definition.InteractionName) ? item.Definition.ItemName : item.Definition.InteractionName;
    public bool Attach(Item item)
    {
        if (!Recognizes(item)) {
            return false;
        }

        var game = Name(item).StartsWith("wf_game_", StringComparison.Ordinal);
        var seconds = int.TryParse(item.LegacyDataString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var saved) ? Math.Max(0, saved) : 0;
        _clocks[item.Id] = new(item, game, game ? seconds : (int)Math.Min(maxHalfSeconds, seconds * 2L));

        return true;
    }
    public long? ReadMilliseconds(Item item) => Find(item) is { Game: false } clock ? clock.Value * 500L : null;
    public bool IsRunning(Item item) => Find(item)?.Running == true;
    public void Forget(Item item)
    {
        if (Find(item) != null) {
            _clocks.Remove(item.Id);
        }
    }
    public void Clear()
    {
        _clocks.Clear();
        _changes.Clear();
    }
    private Clock? Find(Item item) => _clocks.TryGetValue(item.Id, out var clock) && ReferenceEquals(clock.Item, item) ? clock : null;

    // Raw Volt clock control: 0 start, 1 stop, 2 reset, 3 restart, 4 toggle.
    public bool Control(Item item, int operation, long nowMilliseconds)
    {
        var clock = Find(item);

        if (clock == null || clock.Game || operation is < 0 or > 4) {
            return false;
        }

        if (operation == 4) {
            operation = clock.Running ? 1 : 0;
        }

        if (operation is 2 or 3) {
            var previous = clock.Value;
            clock.Value = 0;
            clock.Running = false;
            PublishClock(clock, previous);
        }

        if (operation is 0 or 3 && !clock.Running) {
            clock.Running = true;
            clock.NextTick = nowMilliseconds + 500;
        }

        if (operation == 1) {
            clock.Running = false;
        }

        return true;
    }
    // Polaris wire operator: 0 increase, 1 decrease, 2 set (Turbo's internal enum differs).
    public bool Adjust(Item item, int operation, int minutes, int halfSecondSteps)
    {
        var clock = Find(item);

        if (clock == null || clock.Game || operation is < 0 or > 2 || minutes is < 0 or > 99 || halfSecondSteps is < 0 or > 119) {
            return false;
        }

        var amount = minutes * 120L + halfSecondSteps;
        var previous = clock.Value;
        clock.Value = (int)Math.Clamp(operation switch { 0 => previous + amount, 1 => previous - amount, _ => amount }, 0, maxHalfSeconds);
        PublishClock(clock, previous);

        return true;
    }
    public bool Use(Item item, int parameter, long nowMilliseconds)
    {
        var clock = Find(item);

        if (clock == null) {
            return false;
        }

        if (!clock.Game) {
            return Control(item, parameter == 2 ? 2 : 4, nowMilliseconds);
        }

        if (clock.Running) {
            clock.Running = false;
            _changes.Enqueue(new(item, new(WiredEventKind.GameEnd) { EventItem = item }, false));

            if (parameter != 2) {
                return true;
            }
        }

        if (parameter == 2) {
            clock.Value = defaultGameSeconds;
            PublishDisplay(clock, new(WiredEventKind.StateChanged) { EventItem = item });

            return true;
        }

        clock.Value = int.TryParse(item.LegacyDataString, out var duration) && duration > 0 ? duration : defaultGameSeconds;
        clock.Running = true;
        clock.NextTick = nowMilliseconds + 1000;
        _changes.Enqueue(new(item, new(WiredEventKind.GameStart) { EventItem = item }, false));

        return true;
    }
    public IReadOnlyList<WiredCounterChange> Poll(long nowMilliseconds)
    {
        foreach (var clock in _clocks.Values) {
            if (!clock.Running || nowMilliseconds < clock.NextTick) {
                continue;
            }

            if (!clock.Game && clock.Value >= maxHalfSeconds) {
                clock.Running = false;
                continue;
            }

            var previous = clock.Value;
            clock.Value += clock.Game ? -1 : 1;
            clock.Value = Math.Max(0, clock.Value);
            clock.NextTick = nowMilliseconds + (clock.Game ? 1000 : 500);

            if (!clock.Game) {
                PublishClock(clock, previous);
            }
            else {
                PublishDisplay(clock, new(WiredEventKind.StateChanged) { EventItem = clock.Item });

                if (clock.Value == 0) {
                    clock.Running = false;
                    _changes.Enqueue(new(clock.Item, new(WiredEventKind.GameEnd) { EventItem = clock.Item }, false));
                }
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
    private void PublishClock(Clock clock, int previous) => PublishDisplay(clock,
        new(WiredEventKind.Counter) { EventItem = clock.Item, PreviousValue = previous * 500L, Value = clock.Value * 500L });
    private void PublishDisplay(Clock clock, WiredRuntimeEvent @event)
    {
        var text = (clock.Game ? clock.Value : clock.Value / 2).ToString(CultureInfo.InvariantCulture);
        var changed = clock.Item.LegacyDataString != text;

        if (changed) {
            clock.Item.LegacyDataString = text;
        }

        _changes.Enqueue(new(clock.Item, @event, changed));
    }
}
