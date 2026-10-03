using System.Collections.Concurrent;
using Plus.Core;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Initiator decides what a refusal means: explicit closes just fail, automatic closes are kept and retried.
public enum GateCloseReason { Click, Wired, Automatic }

public enum GateTransition { Applied, Refused, Queued, Cancelled, Unchanged }

// Every writer that closes a gate goes through TryClose on the room task (§16.3).
public sealed class GateTransitionService(Room room, Func<IGateOccupancy> occupancy)
{
    private const string OpenState = "1";
    // A class on purpose: queued requests are matched by identity, never by value.
    private sealed class CloseRequest(Item item, GateCloseReason reason, string closedState, bool persist, Action<Item>? afterClose)
    {
        public Item Item { get; } = item;
        public GateCloseReason Reason { get; } = reason;
        public string ClosedState { get; } = closedState;
        public bool Persist { get; } = persist;
        public Action<Item>? AfterClose { get; } = afterClose;
    }

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly List<CloseRequest> _retained = new();
    // At most one explicit (click/Wired) close per gate waits in the queue; a later toggle cancels it.
    private readonly ConcurrentDictionary<uint, CloseRequest> _explicit = new();
    private int _cancelledInQueue;
    // Pending-toggle resolution, the state observation and every commit share this lock (before PlacementSync).
    private readonly object _sync = new();

    public int PendingCount => _queue.Count - Volatile.Read(ref _cancelledInQueue) + _retained.Count;

    public static bool IsGate(Item item)
        => item.Definition.InteractionType is InteractionType.Gate or InteractionType.GuildGate or InteractionType.GateVip;

    public static bool IsClosing(Item item, string newState) => IsClosing(item, item.LegacyDataString, newState);

    public static bool IsClosing(Item item, string currentState, string newState)
        => IsGate(item) && currentState == OpenState && newState != OpenState;

    // The state legacy would show: a close that is still queued has already happened there.
    public static string EffectiveState(Item item)
        => (IsGate(item) ? item.GetRoom()?.GetGameMap()?.Gates?.QueuedClosedState(item) : null) ?? item.LegacyDataString;

    private string? QueuedClosedState(Item item) => _explicit.TryGetValue(item.Id, out var queued) ? queued.ClosedState : null;

    // Opening and non-gate writes are unchanged; only a closing transition is guarded.
    public static GateTransition Apply(Item item, string state, GateCloseReason reason, bool persist = true, Action<Item>? afterWrite = null)
    {
        var gates = item.GetRoom()?.GetGameMap()?.Gates;
        if (gates != null && IsClosing(item, state)) return gates.TryClose(item, reason, state, persist, afterWrite);
        if (gates != null && IsGate(item) && state == OpenState) return gates.Open(item, state, persist, afterWrite);
        item.LegacyDataString = state;
        item.UpdateState(persist, true);
        afterWrite?.Invoke(item);
        return GateTransition.Applied;
    }

    // Toggle-style writers decide the next state from the state they observe. For gates that decision is
    // atomic with the pending-close check and the commit; other furniture is a plain write.
    public static GateTransition ToggleState(Item item, Func<string, string?> nextState, GateCloseReason reason,
        bool persist = true, Action<Item>? afterWrite = null)
    {
        var gates = IsGate(item) ? item.GetRoom()?.GetGameMap()?.Gates : null;
        if (gates != null) return gates.Toggle(item, nextState, reason, persist, afterWrite);
        return nextState(item.LegacyDataString) is { } state ? Apply(item, state, reason, persist, afterWrite) : GateTransition.Unchanged;
    }

    // An absolute opening is the latest word: it cancels a close that is still queued for this gate.
    private GateTransition Open(Item item, string state, bool persist, Action<Item>? afterWrite)
    {
        LegacyDataFormat? data;
        lock (_sync)
        {
            if (_explicit.TryRemove(item.Id, out _)) Interlocked.Increment(ref _cancelledInQueue);
            data = item.StoreStateQuietly(state);
        }
        Publish(item, data, persist, afterWrite, rebuildGrid: false);
        return GateTransition.Applied;
    }

    public GateTransition TryClose(Item item, GateCloseReason reason, string closedState = "0", bool persist = true, Action<Item>? afterClose = null)
    {
        var request = new CloseRequest(item, reason, closedState, persist, afterClose);
        if (RoomOwnerScope.IsOwner(room)) return Close(request);
        lock (_sync) Enqueue(request);
        return GateTransition.Queued;
    }

    // A toggle that finds a close still queued is the "open" half of a double click: it cancels that close.
    public GateTransition Toggle(Item item, Func<string, string?> nextState, GateCloseReason reason,
        bool persist = true, Action<Item>? afterWrite = null)
    {
        (bool Refused, LegacyDataFormat? Data) committed;
        bool closing;
        lock (_sync)
        {
            if (_explicit.TryRemove(item.Id, out _))
            {
                Interlocked.Increment(ref _cancelledInQueue);
                return GateTransition.Cancelled;
            }
            var state = nextState(item.LegacyDataString);
            if (state is null) return GateTransition.Unchanged;
            closing = IsClosing(item, state);
            if (closing && !RoomOwnerScope.IsOwner(room))
            {
                Enqueue(new(item, reason, state, persist, afterWrite));
                return GateTransition.Queued;
            }
            committed = closing ? ValidateAndWrite(item, state) : (false, item.StoreStateQuietly(state));
            if (committed.Refused) return GateTransition.Refused;
        }
        Publish(item, committed.Data, persist, afterWrite, rebuildGrid: closing);
        return GateTransition.Applied;
    }

    private void Enqueue(CloseRequest request)
    {
        if (request.Reason == GateCloseReason.Automatic || _explicit.TryAdd(request.Item.Id, request))
            _queue.Enqueue(() => Run(request));
    }

    // Runs `work` now on the room task, otherwise on the next drain, in order with queued closes.
    public void Post(Action work)
    {
        if (RoomOwnerScope.IsOwner(room)) work();
        else _queue.Enqueue(work);
    }

    // Room task, once per tick: requests from other threads, then automatic closes kept from earlier ticks.
    public void Drain()
    {
        var due = _retained.Select(request => (Action)(() => Run(request))).ToList();
        _retained.Clear();
        while (_queue.TryDequeue(out var work)) due.Add(work);
        foreach (var work in due) RunGuarded(work);
    }

    private static void RunGuarded(Action work)
    {
        try { work(); }
        catch (Exception error) { ExceptionLogger.LogException(error); }
    }

    // Taking the pending marker and committing are one step, so a toggle sees either "still queued" or "closed".
    private void Run(CloseRequest request)
    {
        (bool Refused, LegacyDataFormat? Data) committed;
        lock (_sync)
        {
            if (request.Reason != GateCloseReason.Automatic && !TakeExplicit(request)) return;
            if (!ReferenceEquals(room.GetRoomItemHandler().GetItem(request.Item.Id), request.Item)) return;
            committed = ValidateAndWrite(request.Item, request.ClosedState);
        }
        if (committed.Refused)
        {
            if (request.Reason == GateCloseReason.Automatic) _retained.Add(request);
            return;
        }
        Publish(request.Item, committed.Data, request.Persist, request.AfterClose, rebuildGrid: true);
    }

    private bool TakeExplicit(CloseRequest request)
    {
        if (_explicit.TryGetValue(request.Item.Id, out var queued) && ReferenceEquals(queued, request)
            && ((ICollection<KeyValuePair<uint, CloseRequest>>)_explicit).Remove(new(request.Item.Id, request)))
            return true;
        Interlocked.Decrement(ref _cancelledInQueue);
        return false;
    }

    private GateTransition Close(CloseRequest request)
    {
        (bool Refused, LegacyDataFormat? Data) committed;
        lock (_sync) committed = ValidateAndWrite(request.Item, request.ClosedState);
        if (committed.Refused) return GateTransition.Refused;
        Publish(request.Item, committed.Data, request.Persist, request.AfterClose, rebuildGrid: true);
        return GateTransition.Applied;
    }

    // Everything that notifies, persists or broadcasts happens here, after the locks are released.
    private void Publish(Item item, LegacyDataFormat? data, bool persist, Action<Item>? after, bool rebuildGrid)
    {
        data?.NotifyDataUpdated();
        item.UpdateState(persist, true);
        if (rebuildGrid) room.GetGameMap().Navigation?.ApplyDirty();
        after?.Invoke(item);
    }

    // Footprint capture, occupancy validation and the state publication form one transaction under
    // PlacementSync (then NavSync inside the write), so a packet-thread move cannot slip between them.
    private (bool Refused, LegacyDataFormat? Data) ValidateAndWrite(Item item, string closedState)
    {
        lock (room.GetGameMap().PlacementSync)
        {
            if (occupancy().IsBlocked(item.GetCoords)) return (true, null);
            return (false, item.StoreStateQuietly(closedState));
        }
    }
}
