namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class ForcePlacementService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private NavGrid Grid => navigation.Grid;
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
        if (!Grid.InBounds(actor.X, actor.Y)) return;
        var tile = Grid.Tile(actor.X, actor.Y);
        if (!Grid.Active(tile)) return;
        if (resolution == ForceResolution.ExactZ && Math.Abs(Grid.WalkZ[tile] - z) > .001) return;
        if (resolution == ForceResolution.NearestAtOrBelow && Grid.WalkZ[tile] > z + .001) return;
        state.CurrentRef = Grid.Reference(tile); state.SupportZ = Grid.WalkZ[tile];
        PostureService.Apply(room, Grid, actor);
    }

}
