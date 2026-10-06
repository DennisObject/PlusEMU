using System.Drawing;

namespace Plus.HabboHotel.Rooms.PathFinding;

public static class InteractionMovement
{
    public static void RequestInteractionStep(this RoomUser actor, Room room, Point target, bool allowOccupied = false)
    {
        if (room.GetGameMap().Navigation is { UsesExecutor: true } navigation)
        {
            actor.AllowOverride = false;
            navigation.InteractionStep(actor, target.X, target.Y);

            return;
        }

        actor.MoveTo(target.X, target.Y, allowOccupied);
    }

}
