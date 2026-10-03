using System.Collections.Concurrent;
using Plus.Core;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Initiator decides what a refusal means: explicit closes just fail, automatic closes are kept and retried.
public enum GateCloseReason { Click, Wired, Automatic }

public enum GateTransition { Applied, Refused, Queued }

// Every writer that closes a gate goes through TryClose on the room task (§16.3).
public sealed class GateTransitionService(Room room, Func<IGateOccupancy> occupancy)
{
    private const string OpenState = "1";
    private readonly record struct CloseRequest(Item Item, GateCloseReason Reason, string ClosedState, bool Persist, Action<Item>? AfterClose);
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly List<CloseRequest> _retained = new();

    public int PendingCount => _queue.Count + _retained.Count;

    public static bool IsGate(Item item)
        => item.Definition.InteractionType is InteractionType.Gate or InteractionType.GuildGate or InteractionType.GateVip;

    public static bool IsClosing(Item item, string newState)
        => IsGate(item) && item.LegacyDataString == OpenState && newState != OpenState;

    // Opening and non-gate writes are unchanged; only a closing transition is guarded.
    public static GateTransition Apply(Item item, string state, GateCloseReason reason, bool persist = true, Action<Item>? afterWrite = null)
    {
        var gates = item.GetRoom()?.GetGameMap()?.Gates;
        if (gates != null && IsClosing(item, state)) return gates.TryClose(item, reason, state, persist, afterWrite);
        item.LegacyDataString = state;
        item.UpdateState(persist, true);
        afterWrite?.Invoke(item);
        return GateTransition.Applied;
    }

    public GateTransition TryClose(Item item, GateCloseReason reason, string closedState = "0", bool persist = true, Action<Item>? afterClose = null)
    {
        var request = new CloseRequest(item, reason, closedState, persist, afterClose);
        if (RoomOwnerScope.IsOwner(room)) return Close(request);
        _queue.Enqueue(() => Run(request));
        return GateTransition.Queued;
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

    private void Run(CloseRequest request)
    {
        if (!ReferenceEquals(room.GetRoomItemHandler().GetItem(request.Item.Id), request.Item)) return;
        if (Close(request) == GateTransition.Refused && request.Reason == GateCloseReason.Automatic)
            _retained.Add(request);
    }

    private GateTransition Close(CloseRequest request)
    {
        var item = request.Item;
        var changed = ValidateAndWrite(item, request.ClosedState);
        if (changed.Refused) return GateTransition.Refused;
        changed.Data?.NotifyDataUpdated();
        item.UpdateState(request.Persist, true);
        room.GetGameMap().Navigation?.ApplyDirty();
        request.AfterClose?.Invoke(item);
        return GateTransition.Applied;
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
