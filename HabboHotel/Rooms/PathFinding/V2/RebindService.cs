namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class RebindService(Room room, NavGrid Grid)
{
    public void Rebind(RoomUser actor)
    {
        var state = actor.Movement;
        if (state.BoundVersion == Grid.Version) return;
        state.BoundVersion = Grid.Version;
        if (state.CurrentRef is not { } current) return;
        var oldZ = actor.Z;
        if (Grid.Active(current.Tile))
        {
            state.CurrentRef = Grid.Reference(current.Tile); state.SupportZ = Grid.WalkZ[current.Tile];
            PostureService.Apply(room, Grid, actor);
        }
        else state.CurrentRef = null;
        if (actor.Z != oldZ) actor.UpdateNeeded = true;
    }
}
