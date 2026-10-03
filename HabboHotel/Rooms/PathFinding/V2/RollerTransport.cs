using System.Drawing;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class RollerTransport(Room room, RoomNavigation navigation)
{
    private MovementContext Context => navigation.Executor.Context;

    internal IServerPacket? TryTransport(RoomUser actor, Item roller, Point destination,
        double sourceZ, double carriedZ)
    {
        var grid = navigation.Grid;
        if (actor.Movement.State != NavState.Active || actor.IsWalking || actor.Movement.PendingCount != 0
            || !grid.InBounds(destination.X, destination.Y)
            || !room.GetGameMap().CanRollItemHere(destination.X, destination.Y)) return null;
        var slot = grid.Tile(destination.X, destination.Y);
        var profile = Context.Profiles.Refresh(actor);
        var from = new NavPosition(actor.X, actor.Y, sourceZ);
        if (!new MovementRules(grid, navigation.Settings).CanStep(profile, from, grid.Position(slot),
                StepPurpose.Roller, OccupancyView.Execution, Context.OccupancyAt(actor, slot)).Ok) return null;
        var mask = ClaimMatrix.BlockingMask(profile, grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);
        if (!Context.Claims.TryClaim(actor, slot, ClaimKind.Roller, mask)) return null;
        var message = new SlideObjectBundleComposer(actor.X, actor.Y, sourceZ, destination.X,
            destination.Y, carriedZ, roller.Id, actor.VirtualId, 0);
        navigation.ForcePlace(actor, destination.X, destination.Y, carriedZ, ForceResolution.ExactZ);
        actor.IsRolling = true; actor.RollerDelay = 1;
        Context.TransportLandings.Trigger(actor, destination, roller);
        return message;
    }

}
