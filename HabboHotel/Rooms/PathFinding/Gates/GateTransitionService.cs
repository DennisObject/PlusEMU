using System.Collections.Concurrent;
using Plus.Core;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Initiator decides what a refusal means: explicit closes just fail, automatic closes are kept and retried.
public enum GateCloseReason { Click, Wired, Automatic }

public enum GateTransition { Applied, Refused, Queued, Unchanged }

// Every gate state write goes through one per-gate FIFO (§16.3): evaluated once, in order, against committed
// state. A gate is busy from the moment an operation starts or is queued until its broadcast and callbacks end.
public sealed class GateTransitionService(Room room, Func<IGateOccupancy> occupancy)
{
    private const string OpenState = "1";

    private sealed class Entry(Item item, Func<string, string?> next, GateCloseReason reason, bool persist, Action<Item>? after)
    {
        public Item Item { get; } = item;
        public Func<string, string?> Next { get; } = next;
        public GateCloseReason Reason { get; } = reason;
        public bool Persist { get; } = persist;
        public Action<Item>? After { get; } = after;
        public Entry Prepared(string state) => new(Item, _ => state, Reason, Persist, After);
    }

    private readonly record struct Outcome(GateTransition Result, LegacyDataFormat? Data = null, bool Closed = false);

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly Dictionary<uint, Entry> _retained = new();
    private readonly Dictionary<uint, int> _busy = new();
    // Queue inspection, enqueue and every owner-side commit share this lock (before PlacementSync, then NavSync).
    private readonly object _sync = new();
    [ThreadStatic] private static uint _replaying;

    // Test seam: runs on the caller right before it contends for the gate lock.
    internal Action? DecisionHook { get; set; }

    public int PendingCount => _queue.Count + _retained.Count;

    public static bool IsGate(Item item)
        => item.Definition.InteractionType is InteractionType.Gate or InteractionType.GuildGate or InteractionType.GateVip;

    public static bool IsClosing(Item item, string newState) => IsClosing(item, item.LegacyDataString, newState);

    public static bool IsClosing(Item item, string currentState, string newState)
        => IsGate(item) && currentState == OpenState && newState != OpenState;

    // Absolute write: sequenced for gates, a plain write for everything else.
    public static GateTransition Apply(Item item, string state, GateCloseReason reason, bool persist = true, Action<Item>? afterWrite = null)
        => ToggleState(item, _ => state, reason, persist, afterWrite);

    // Toggle-style writers decide the next state from the state they observe; null leaves it unchanged.
    public static GateTransition ToggleState(Item item, Func<string, string?> nextState, GateCloseReason reason,
        bool persist = true, Action<Item>? afterWrite = null)
    {
        if (IsGate(item) && item.GetRoom()?.GetGameMap()?.Gates is { } gates)
            return gates.Toggle(item, nextState, reason, persist, afterWrite);
        if (nextState(item.LegacyDataString) is not { } state) return GateTransition.Unchanged;
        item.LegacyDataString = state;
        item.UpdateState(persist, true);
        afterWrite?.Invoke(item);
        return GateTransition.Applied;
    }

    // For a writer whose sequencing was already decided (see TryDefer): commits without queueing.
    public static GateTransition WriteNow(Item item, string state, GateCloseReason reason, bool persist = true)
        => IsGate(item) && item.GetRoom()?.GetGameMap()?.Gates is { } gates
            ? gates.CommitNow(item, state, persist)
            : ToggleState(item, _ => state, reason, persist);

    public GateTransition TryClose(Item item, GateCloseReason reason, string closedState = "0", bool persist = true, Action<Item>? afterClose = null)
        => Toggle(item, _ => closedState, reason, persist, afterClose);

    public GateTransition Toggle(Item item, Func<string, string?> nextState, GateCloseReason reason,
        bool persist = true, Action<Item>? afterWrite = null)
    {
        DecisionHook?.Invoke();
        var entry = new Entry(item, nextState, reason, persist, afterWrite);
        Outcome outcome;
        lock (_sync)
        {
            if (IsBusy(item.Id)) { Enqueue(item.Id, () => RunQueued(entry)); return GateTransition.Queued; }
            var state = nextState(item.LegacyDataString);
            if (state is null) return GateTransition.Unchanged;
            if (IsClosing(item, state) && !RoomOwnerScope.IsOwner(room))
            {
                Enqueue(item.Id, () => RunQueued(entry.Prepared(state)));
                return GateTransition.Queued;
            }
            outcome = Commit(item, state);
            if (outcome.Result == GateTransition.Applied) Begin(item.Id);
        }
        return outcome.Result == GateTransition.Applied ? Complete(entry, outcome) : outcome.Result;
    }

    // Unsequenced commit for a writer that already passed TryDefer; still holds the gate busy until it publishes.
    private GateTransition CommitNow(Item item, string state, bool persist)
    {
        Outcome outcome;
        var replay = _replaying == item.Id;
        lock (_sync)
        {
            outcome = Commit(item, state);
            if (outcome.Result == GateTransition.Applied && !replay) Begin(item.Id);
        }
        if (outcome.Result != GateTransition.Applied) return outcome.Result;
        try { Publish(item, outcome, persist, null); }
        finally { if (!replay) End(item.Id); }
        return GateTransition.Applied;
    }

    // A writer whose whole transaction (read, transform, write, notify) must stay together asks here first.
    // True: the transaction was queued (`replayFor(null)` = unevaluated, `replayFor(state)` = prepared closing value).
    // False: run it now; `prepared` carries the value already evaluated, or null if nothing was evaluated.
    public bool TryDefer(Item item, Func<string, string?> peek, Func<string?, Action> replayFor, out string? prepared)
    {
        prepared = null;
        lock (_sync)
        {
            if (IsBusy(item.Id)) { EnqueueReplay(item.Id, replayFor(null)); return true; }
            if (RoomOwnerScope.IsOwner(room)) return false;
            prepared = peek(item.LegacyDataString);
            if (prepared is null || !IsClosing(item, prepared)) return false;
            EnqueueReplay(item.Id, replayFor(prepared));
            return true;
        }
    }

    // Room task, once per tick: the entries queued before this drain, then retained automatic closes.
    public void Drain()
    {
        var retry = _retained.Values.ToList();
        _retained.Clear();
        for (var remaining = _queue.Count; remaining > 0 && _queue.TryDequeue(out var work); remaining--) RunGuarded(work);
        foreach (var entry in retry) RunGuarded(() => Retry(entry));
    }

    private static void RunGuarded(Action work)
    {
        try { work(); }
        catch (Exception error) { ExceptionLogger.LogException(error); }
    }

    private bool IsBusy(uint id) => _replaying != id && _busy.GetValueOrDefault(id) > 0;

    private void Begin(uint id) => _busy[id] = _busy.GetValueOrDefault(id) + 1;

    private void End(uint id)
    {
        lock (_sync)
        {
            if (_busy.GetValueOrDefault(id) <= 1) _busy.Remove(id);
            else _busy[id]--;
        }
    }

    private void Enqueue(uint id, Action work) { Begin(id); _queue.Enqueue(work); }

    private void EnqueueReplay(uint id, Action replay) => Enqueue(id, () =>
    {
        _replaying = id;
        try { replay(); }
        finally { _replaying = 0; End(id); }
    });

    // Dequeued entries stay busy through their own broadcast and callbacks.
    private void RunQueued(Entry entry)
    {
        try
        {
            Outcome outcome;
            lock (_sync) outcome = StillInRoom(entry.Item) ? EvaluateAndCommit(entry) : new(GateTransition.Unchanged);
            if (outcome.Result == GateTransition.Refused && entry.Reason == GateCloseReason.Automatic) _retained[entry.Item.Id] = entry;
            else Complete(entry, outcome, releases: false);
        }
        finally { End(entry.Item.Id); }
    }

    private void Retry(Entry entry)
    {
        Outcome outcome;
        lock (_sync)
        {
            if (IsBusy(entry.Item.Id)) { _retained[entry.Item.Id] = entry; return; }
            outcome = StillInRoom(entry.Item) ? EvaluateAndCommit(entry) : new(GateTransition.Unchanged);
            if (outcome.Result == GateTransition.Applied) Begin(entry.Item.Id);
        }
        if (outcome.Result == GateTransition.Refused) _retained[entry.Item.Id] = entry;
        else if (outcome.Result == GateTransition.Applied) Complete(entry, outcome);
    }

    private bool StillInRoom(Item item) => ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item);

    private Outcome EvaluateAndCommit(Entry entry)
        => entry.Next(entry.Item.LegacyDataString) is { } state ? Commit(entry.Item, state) : new(GateTransition.Unchanged);

    private Outcome Commit(Item item, string state)
    {
        if (!IsClosing(item, state)) return new(GateTransition.Applied, item.StoreStateQuietly(state));
        var (refused, data) = ValidateAndWrite(item, state);
        return refused ? new(GateTransition.Refused) : new(GateTransition.Applied, data, Closed: true);
    }

    // Footprint capture, occupancy validation and the state write (including the NavInputs record publication
    // inside NavSync) form one transaction under PlacementSync, so a packet-thread move cannot slip between them.
    private (bool Refused, LegacyDataFormat? Data) ValidateAndWrite(Item item, string closedState)
    {
        lock (room.GetGameMap().PlacementSync)
        {
            if (occupancy().IsBlocked(item.GetCoords)) return (true, null);
            return (false, item.StoreStateQuietly(closedState));
        }
    }

    private GateTransition Complete(Entry entry, Outcome outcome, bool releases = true)
    {
        try { if (outcome.Result == GateTransition.Applied) Publish(entry.Item, outcome, entry.Persist, entry.After); }
        finally { if (releases) End(entry.Item.Id); }
        return outcome.Result;
    }

    // Notifications, persistence, broadcast, ApplyDirty and follow-ups: never under a lock.
    private void Publish(Item item, Outcome outcome, bool persist, Action<Item>? after)
    {
        outcome.Data?.NotifyDataUpdated();
        item.UpdateState(persist, true);
        if (outcome.Closed) room.GetGameMap().Navigation?.ApplyDirty();
        after?.Invoke(item);
    }
}
