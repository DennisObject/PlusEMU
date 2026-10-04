using Plus.Communication.Packets.Outgoing.Rooms.Engine;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class ForcePlacementService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private NavGrid Grid => navigation.Grid;
    internal void Teleport(RoomUser actor, MoveCommand command)
    {
        if (!Grid.InBounds(command.X, command.Y)) { cancellation.Cancel(actor, command.Sequence); return; }
        var z = Grid.WalkZ[Grid.TopSlot(Grid.Tile(command.X, command.Y))];
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
        Relocate(actor, command.X, command.Y, command.Z, command.CommandSequence);
        Bind(actor, command.Z, command.Resolution);
        context.RefreshMembership(actor);
    }

    // Moves the actor without binding a surface; callers bind after publishing the final geometry.
    internal void Relocate(RoomUser actor, int x, int y, double z, long sequence)
    {
        cancellation.Cancel(actor, sequence);
        room.GetGameMap().RemoveUserFromMap(actor, new(actor.X, actor.Y));
        actor.InitializePosition(x, y, z);
        actor.GoalX = x; actor.GoalY = y;
        actor.Movement.LocationRevision++;
        actor.Movement.CurrentRef = null; actor.Movement.SupportZ = z;
        room.GetGameMap().AddUserToMap(actor, new(x, y));
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
        var slot = SurfaceSelection.Select(Grid, Grid.Tile(actor.X, actor.Y), z, resolution);
        if (slot < 0) return;
        actor.Movement.CurrentRef = Grid.Reference(slot); actor.Movement.SupportZ = Grid.WalkZ[slot];
    }

}
