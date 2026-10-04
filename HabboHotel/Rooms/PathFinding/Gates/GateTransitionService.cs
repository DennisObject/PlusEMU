using Plus.Core;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Initiator decides what a refusal means: explicit closes just fail, automatic closes are kept and retried.
public enum GateCloseReason { Click, Wired, Automatic, Walk }

public enum GateTransition { Applied, Refused, Queued, Unchanged }

// The capability to write one gate. At most one is active per gate; it commits through it and clears it only
// after the operation's publication, callbacks and (for variable transactions) completion have all finished.
internal sealed class GateOperation(Item item)
{
    public Item Item { get; } = item;
    public bool Entered { get; set; }
    public bool Committed { get; set; }
}

// Every gate state write goes through one per-gate FIFO (§16.3): evaluated once, in order, against committed state.
public sealed class GateTransitionService(Room room, Func<IGateOccupancy> occupancy)
{
    private const string OpenState = "1";

    private sealed class Entry(Item item, Action<GateOperation> run)
    {
        public Item Item { get; } = item;
        public Action<GateOperation> Run { get; } = run;
    }

    private sealed class Lane
    {
        public GateOperation? Active { get; set; }
        public LinkedList<Entry> Pending { get; } = new();
    }

    private sealed class StateWrite(Item item, Func<string, string?> next, GateCloseReason reason, bool persist, Action<Item>? after)
    {
        public Item Item { get; } = item;
        public Func<string, string?> Next { get; } = next;
        public GateCloseReason Reason { get; } = reason;
        public bool Persist { get; } = persist;
        public Action<Item>? After { get; } = after;
        public StateWrite Prepared(string state) => new(Item, _ => state, Reason, Persist, After);
    }

    private readonly record struct Outcome(GateTransition Result, LegacyDataFormat? Data = null, bool Closed = false);

    private readonly Dictionary<uint, Lane> _lanes = new();
    private readonly Queue<uint> _order = new();
    private readonly Dictionary<uint, StateWrite> _retained = new();
    // Admission, enqueue and every commit share this lock (before PlacementSync, then NavSync).
    private readonly object _sync = new();
    [ThreadStatic] private static GateOperation? _current;

    // Test seam: runs on the caller right before it contends for the gate lock.
    internal Action? DecisionHook { get; set; }

    public int PendingCount { get { lock (_sync) return _order.Count + _retained.Count; } }

    // The sequencer exists for v2 only: legacy and shadow rooms keep every original gate code path.
    public static GateTransitionService? For(Room? room)
        => room is { UsesV2Movement: true } ? room.GetGameMap()?.Gates : null;

    public static GateTransitionService? For(Item item) => For(item.GetRoom());

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
        if (IsGate(item) && For(item) is { } gates)
            return gates.Toggle(item, nextState, reason, persist, afterWrite);
        if (nextState(item.LegacyDataString) is not { } state) return GateTransition.Unchanged;
        item.LegacyDataString = state;
        item.UpdateState(persist, true);
        afterWrite?.Invoke(item);
        return GateTransition.Applied;
    }

    // Commit of a variable transaction that holds the gate's operation: usable once, never by nested writes.
    public static GateTransition WriteNow(Item item, string state, GateCloseReason reason, bool persist = true)
    {
        var operation = _current;
        if (operation is { Committed: false } && ReferenceEquals(operation.Item, item)
            && For(item) is { } gates && gates.IsActive(operation))
        {
            operation.Committed = true;
            return gates.CommitWith(operation, state, persist);
        }
        return ToggleState(item, _ => state, reason, persist);
    }

    public GateTransition TryClose(Item item, GateCloseReason reason, string closedState = "0", bool persist = true, Action<Item>? afterClose = null)
        => Toggle(item, _ => closedState, reason, persist, afterClose);

    public GateTransition Toggle(Item item, Func<string, string?> nextState, GateCloseReason reason,
        bool persist = true, Action<Item>? afterWrite = null)
    {
        DecisionHook?.Invoke();
        var write = new StateWrite(item, nextState, reason, persist, afterWrite);
        var operation = Admit(item, run => RunState(write, run));
        if (operation == null) return GateTransition.Queued;
        Outcome outcome;
        try
        {
            var state = nextState(item.LegacyDataString);
            if (state is null) { End(operation); return GateTransition.Unchanged; }
            if (IsClosing(item, state) && !RoomOwnerScope.IsOwner(room))
            {
                var prepared = write.Prepared(state);
                Requeue(operation, run => RunState(prepared, run));
                return GateTransition.Queued;
            }
            outcome = CommitWith(operation, state);
        }
        catch { End(operation); throw; }
        try { if (outcome.Result == GateTransition.Applied) Publish(item, outcome, persist, afterWrite); }
        finally { End(operation); }
        return outcome.Result;
    }

    // A variable transaction (read, transform, write, notify) asks for the gate first. The returned scope holds the
    // operation until the whole transaction ends. `deferred`: it was queued instead; `prepared`: the transform's
    // single, already evaluated result when the write must run now.
    internal IDisposable? AdmitVariableWrite(Item item, Func<string, string?> peek, Func<string?, Action> replayFor,
        Func<bool> stillTargeted, out WiredAdmission admission, out string? prepared)
    {
        admission = WiredAdmission.Proceed; prepared = null;
        var current = _current;
        if (current is { Entered: false } && ReferenceEquals(current.Item, item) && IsActive(current)) { current.Entered = true; return null; }
        var operation = Admit(item, run => RunReplay(replayFor(null), run));
        if (operation == null) { admission = WiredAdmission.Deferred; return null; }
        try
        {
            // Validate the reserved target before the first evaluation, outside the gate lock.
            if (!stillTargeted()) { End(operation); admission = WiredAdmission.Stale; return null; }
            if (!RoomOwnerScope.IsOwner(room))
            {
                prepared = peek(item.LegacyDataString);
                if (prepared is not null && IsClosing(item, prepared))
                {
                    var closing = prepared;
                    Requeue(operation, run => RunReplay(replayFor(closing), run));
                    admission = WiredAdmission.Deferred;
                    return null;
                }
            }
        }
        catch { End(operation); throw; }
        operation.Entered = true;
        return new Scope(this, operation);
    }

    // Room task, once per tick: the entries queued before this drain, one operation per gate at a time,
    // then the retained automatic closes (which also need an idle gate).
    public void Drain()
    {
        List<uint> due;
        lock (_sync) { due = _order.ToList(); _order.Clear(); }
        var retry = _retained.Values.ToList();
        _retained.Clear();
        foreach (var id in due) RunGuarded(() => RunHead(id));
        foreach (var write in retry) RunGuarded(() => Retry(write));
    }

    private static void RunGuarded(Action work)
    {
        try { work(); }
        catch (Exception error) { ExceptionLogger.LogException(error); }
    }

    private Lane LaneOf(uint id)
    {
        if (!_lanes.TryGetValue(id, out var lane)) _lanes[id] = lane = new();
        return lane;
    }

    // Installs a new operation only when the gate is idle and nothing is waiting; otherwise appends and returns null.
    private GateOperation? Admit(Item item, Action<GateOperation> queuedRun)
    {
        lock (_sync)
        {
            var lane = LaneOf(item.Id);
            if (lane.Active != null || lane.Pending.Count > 0) { Append(lane, item, queuedRun); return null; }
            return lane.Active = new GateOperation(item);
        }
    }

    private void Append(Lane lane, Item item, Action<GateOperation> run)
    {
        lane.Pending.AddLast(new Entry(item, run));
        _order.Enqueue(item.Id);
    }

    // Converts the operation into prepared owner work at the HEAD, ahead of everything that queued behind it
    // while it evaluated, and gives the operation back in the same step so nothing can pass it.
    private void Requeue(GateOperation operation, Action<GateOperation> run)
    {
        lock (_sync)
        {
            var lane = LaneOf(operation.Item.Id);
            if (ReferenceEquals(lane.Active, operation)) lane.Active = null;
            lane.Pending.AddFirst(new Entry(operation.Item, run));
            _order.Enqueue(operation.Item.Id);
        }
    }

    private bool IsActive(GateOperation operation)
    {
        lock (_sync) return _lanes.TryGetValue(operation.Item.Id, out var lane) && ReferenceEquals(lane.Active, operation);
    }

    private void End(GateOperation operation)
    {
        lock (_sync)
        {
            if (!_lanes.TryGetValue(operation.Item.Id, out var lane)) return;
            if (ReferenceEquals(lane.Active, operation)) lane.Active = null;
            if (lane.Active == null && lane.Pending.Count == 0) _lanes.Remove(operation.Item.Id);
        }
    }

    private void RunHead(uint id)
    {
        Entry entry; GateOperation operation;
        lock (_sync)
        {
            if (!_lanes.TryGetValue(id, out var lane)) return;
            if (lane.Active != null) { _order.Enqueue(id); return; }
            if (lane.Pending.Count == 0) return;
            entry = lane.Pending.First!.Value;
            lane.Pending.RemoveFirst();
            operation = lane.Active = new GateOperation(entry.Item);
        }
        try { entry.Run(operation); }
        finally { End(operation); }
    }

    private void Retry(StateWrite write)
    {
        GateOperation operation;
        lock (_sync)
        {
            var lane = LaneOf(write.Item.Id);
            // Not part of the FIFO, but it never commits ahead of an operation or an entry waiting for the gate.
            if (lane.Active != null || lane.Pending.Count > 0) { _retained[write.Item.Id] = write; return; }
            operation = lane.Active = new GateOperation(write.Item);
        }
        try { RunState(write, operation); }
        finally { End(operation); }
    }

    private void RunState(StateWrite write, GateOperation operation)
    {
        var item = write.Item;
        var state = StillInRoom(item) ? write.Next(item.LegacyDataString) : null;
        var outcome = state is null ? new Outcome(GateTransition.Unchanged) : CommitWith(operation, state);
        if (outcome.Result == GateTransition.Refused && write.Reason == GateCloseReason.Automatic) _retained[item.Id] = write;
        else if (outcome.Result == GateTransition.Applied) Publish(item, outcome, write.Persist, write.After);
    }

    private void RunReplay(Action replay, GateOperation operation)
    {
        var previous = _current;
        _current = operation;
        try { replay(); }
        finally { _current = previous; }
    }

    private bool StillInRoom(Item item) => ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item);

    private GateTransition CommitWith(GateOperation operation, string state, bool persist)
    {
        var outcome = CommitWith(operation, state);
        if (outcome.Result == GateTransition.Applied) Publish(operation.Item, outcome, persist, null);
        return outcome.Result;
    }

    private Outcome CommitWith(GateOperation operation, string state)
    {
        lock (_sync)
        {
            if (!_lanes.TryGetValue(operation.Item.Id, out var lane) || !ReferenceEquals(lane.Active, operation))
                throw new InvalidOperationException("Only the active gate operation may commit.");
            var item = operation.Item;
            if (!IsClosing(item, state)) return new(GateTransition.Applied, item.StoreStateQuietly(state));
            var (refused, data) = ValidateAndWrite(item, state);
            return refused ? new(GateTransition.Refused) : new(GateTransition.Applied, data, Closed: true);
        }
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

    // Notifications, persistence, broadcast, ApplyDirty and follow-ups: never under a lock.
    private void Publish(Item item, Outcome outcome, bool persist, Action<Item>? after)
    {
        outcome.Data?.NotifyDataUpdated();
        item.UpdateState(persist, true);
        if (outcome.Closed) room.GetGameMap().Navigation?.ApplyDirty();
        after?.Invoke(item);
    }

    private sealed class Scope : IDisposable
    {
        private readonly GateTransitionService _service;
        private readonly GateOperation _operation;
        private readonly GateOperation? _previous;

        public Scope(GateTransitionService service, GateOperation operation)
        {
            _service = service; _operation = operation; _previous = _current; _current = operation;
        }

        public void Dispose()
        {
            _current = _previous;
            _service.End(_operation);
        }
    }
}
