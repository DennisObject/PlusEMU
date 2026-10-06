using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items;

/// <summary>
/// A user's use that actually writes a new furni state raises one StateChanged. Gates in a v2 room are
/// written by the per-gate sequencer, possibly after the request returned, so their interactors publish
/// from the write itself; every other use is compared before and after the interactor runs.
/// </summary>
internal static class FurnitureStateEvents
{
    public static bool IsSequenced(Item item) => GateTransitionService.IsGate(item) && GateTransitionService.For(item) != null;

    public static RoomUser? Actor(Room room, GameClient? session) =>
        session?.GetHabbo() is { } habbo ? room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) : null;

    public static void PublishIfChanged(Room room, RoomUser? actor, Item item, string before)
    {
        if (!IsSequenced(item) && !string.Equals(before, item.LegacyDataString, StringComparison.Ordinal))
            Publish(room, actor, item);
    }

    // A queued write can land after the furni or its user left. A furni no longer in this room is not
    // reported; a user who left is not named, but the state still changed.
    public static void Publish(Room room, RoomUser? actor, Item item)
    {
        if (!ReferenceEquals(item.GetRoom(), room) || !ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item)) return;
        if (actor != null && (!ReferenceEquals(room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor)
            || !actor.IsBot && !ReferenceEquals(actor.GetClient()?.GetHabbo()?.CurrentRoom, room))) actor = null;
        room.GetWired()?.Dispatch(new(WiredEventKind.StateChanged) { Actor = actor, EventItem = item });
    }
}
