using System.Collections.Concurrent;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Initiator decides what a refusal means: explicit closes just fail, automatic closes are kept and retried.
public enum GateCloseReason { Click, Wired, Automatic }

public enum GateTransition { Applied, Refused, Queued }

// Every writer that closes a gate goes through TryClose on the room task (§16.3).
public sealed class GateTransitionService(Room room, Func<IGateOccupancy> occupancy)
{
    private const string OpenState = "1";
    private readonly record struct CloseRequest(Item Item, GateCloseReason Reason, string ClosedState, bool Persist, Action<Item>? AfterClose);
    private readonly ConcurrentQueue<CloseRequest> _queue = new();
    private readonly List<CloseRequest> _retained = new();

    public int PendingCount => _queue.Count + _retained.Count;

    public static bool IsClosing(Item item, string newState)
        => item.Definition.InteractionType is InteractionType.Gate or InteractionType.GuildGate or InteractionType.GateVip
            && item.LegacyDataString == OpenState && newState != OpenState;

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
        _queue.Enqueue(request);
        return GateTransition.Queued;
    }

    // Room task, once per tick: requests from other threads, then automatic closes kept from earlier ticks.
    public void Drain()
    {
        var due = new List<CloseRequest>(_retained);
        _retained.Clear();
        while (_queue.TryDequeue(out var request)) due.Add(request);
        foreach (var request in due) Run(request);
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
        if (occupancy().IsBlocked(item.GetCoords)) return GateTransition.Refused;
        item.LegacyDataString = request.ClosedState;
        item.UpdateState(request.Persist, true);
        room.GetGameMap().Navigation?.ApplyDirty();
        request.AfterClose?.Invoke(item);
        return GateTransition.Applied;
    }
}
