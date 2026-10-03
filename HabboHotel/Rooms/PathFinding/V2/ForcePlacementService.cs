using Plus.Communication.Packets.Outgoing.Rooms.Engine;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class ForcePlacementService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private NavGrid Grid => navigation.Grid;
    internal void Teleport(RoomUser actor, MoveCommand command)
    {
        if (!Grid.InBounds(command.X, command.Y)) { cancellation.Cancel(actor, command.Sequence); return; }
        var z = Grid.WalkZ[Grid.Tile(command.X, command.Y)];
        var slide = new SlideObjectBundleComposer(actor.X, actor.Y, actor.Z,
            command.X, command.Y, z, 0, actor.VirtualId, 0);
        actor.UnIdle();
        Place(actor, new(RoomCommandKind.ForcePlace, actor, actor.Movement.LifetimeId,
            command.X, command.Y, z, ForceResolution.Highest, command.Sequence));
        context.TransportLandings.Trigger(actor, new(command.X, command.Y));
        room.SendPacket(slide);
    }
    public void Place(RoomUser actor, RoomCommand command)
    {
        cancellation.Cancel(actor, command.CommandSequence);
        room.GetGameMap().RemoveUserFromMap(actor, new(actor.X, actor.Y));
        actor.InitializePosition(command.X, command.Y, command.Z);
        actor.GoalX = command.X; actor.GoalY = command.Y;
        actor.Movement.LocationRevision++;
        Bind(actor, command.Z, command.Resolution);
        room.GetGameMap().AddUserToMap(actor, new(actor.X, actor.Y));
        context.RefreshMembership(actor);
        actor.UpdateNeeded = true;
    }
    internal void Bind(RoomUser actor, double z, ForceResolution resolution)
    {
        var state = actor.Movement;
        state.CurrentRef = null; state.SupportZ = z; state.BoundVersion = Grid.Version;
        ResolveSupport(actor, z, resolution);
        PostureService.Apply(room, Grid, actor);
    }
    private void ResolveSupport(RoomUser actor, double z, ForceResolution resolution)
    {
        if (!Grid.InBounds(actor.X, actor.Y)) return;
        var tile = Grid.Tile(actor.X, actor.Y);
        if (!Grid.Active(tile)) return;
        if (resolution == ForceResolution.ExactZ && Math.Abs(Grid.WalkZ[tile] - z) > .001) return;
        if (resolution == ForceResolution.NearestAtOrBelow && Grid.WalkZ[tile] > z + .001) return;
        actor.Movement.CurrentRef = Grid.Reference(tile); actor.Movement.SupportZ = Grid.WalkZ[tile];
    }

}
