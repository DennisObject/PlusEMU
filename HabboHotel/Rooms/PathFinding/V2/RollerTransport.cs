using System.Drawing;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;

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
        var profile = MovementProfiles.Refresh(room, grid, navigation.Settings, actor);
        var from = new NavPosition(actor.X, actor.Y, sourceZ);
        if (!new MovementRules(grid, navigation.Settings).CanStep(profile, from, grid.Position(slot),
                StepPurpose.Roller, OccupancyView.Execution, Context.OccupancyAt(actor, slot)).Ok) return null;
        var mask = ClaimMatrix.BlockingMask(profile, grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);
        if (!Context.Claims.TryClaim(actor, slot, ClaimKind.Roller, mask)) return null;
        var message = new SlideObjectBundleComposer(actor.X, actor.Y, sourceZ, destination.X,
            destination.Y, carriedZ, roller.Id, actor.VirtualId, 0);
        navigation.ForcePlace(actor, destination.X, destination.Y, carriedZ, ForceResolution.ExactZ);
        actor.IsRolling = true; actor.RollerDelay = 1;
        TriggerLanding(actor, roller, destination);
        return message;
    }

    private void TriggerLanding(RoomUser actor, Item roller, Point destination)
    {
        var habbo = actor.GetClient()?.GetHabbo();
        if (habbo == null) return;
        var revision = actor.Movement.LocationRevision;
        var items = room.GetGameMap().GetRoomItemForSquare(destination.X, destination.Y).ToList();
        foreach (var item in items)
        {
            room.GetWired().TriggerEvent(WiredBoxType.TriggerWalkOnFurni, habbo, item);
            if (actor.Movement.LocationRevision != revision || actor.Movement.State != NavState.Active) return;
        }
        if (ReferenceEquals(room.GetRoomItemHandler().GetItem(roller.Id), roller))
            room.GetWired().TriggerEvent(WiredBoxType.TriggerWalkOffFurni, habbo, roller);
    }
}
