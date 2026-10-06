using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.PathFinding;

public static class PostureService
{
    public static void Apply(Room room, NavGrid grid, RoomUser actor)
    {
        var state = actor.Movement;
        actor.Statusses.Remove("sit");
        actor.Statusses.Remove("lay");
        actor.IsSitting = actor.IsLying = false;

        if (state.CurrentRef is not { } surface) {
            return;
        }

        actor.Z = state.SupportZ + RiderOffset(actor, surface);
        var slot = grid.Layered ? grid.SlotOf(surface) : surface.Tile;

        if (slot < 0) {
            return;
        }

        var flags = grid.Flags[slot];

        if ((flags & (NavFlags.GoalOnlySeat | NavFlags.GoalOnlyBed)) == 0) {
            return;
        }

        var item = room.GetRoomItemHandler().GetItem(surface.SupportItemId);

        if (item != null) {
            ItemPosture(actor, item, flags);
        }
        else if ((flags & NavFlags.ModelSeat) != 0) {
            ModelPosture(room, actor);
        }
    }
    public static double RiderOffset(RoomUser actor, SurfaceRef surface)
        => actor.RidingHorse && !actor.IsBot && surface.Kind != SurfaceKind.WalkMagic ? 1 : 0;
    private static void ItemPosture(RoomUser actor, Plus.HabboHotel.Items.Item item, NavFlags flags)
    {
        var bed = (flags & NavFlags.GoalOnlyBed) != 0;
        actor.SetStatus(bed ? "lay" : "sit", TextHandling.GetString(item.Definition.Height) + (bed ? " null" : ""));
        actor.RotHead = actor.RotBody = item.Rotation;
        actor.UpdateNeeded = true;
    }
    private static void ModelPosture(Room room, RoomUser actor)
    {
        actor.SetStatus("sit", "1.0");
        actor.RotHead = actor.RotBody = room.GetGameMap().Model.SqSeatRot[actor.X, actor.Y];
        actor.UpdateNeeded = true;
    }
}
