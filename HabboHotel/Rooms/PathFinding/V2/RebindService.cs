namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class RebindService(Room room, NavGrid Grid)
{
    public void Rebind(RoomUser actor)
    {
        var state = actor.Movement;
        if (state.BoundVersion == Grid.Version) return;
        RebindAtVersion(actor, Grid.Version, false);
    }

    internal void BeforePublish(RoomUser actor) => RebindAtVersion(actor, Grid.Version + 1, true);

    private void RebindAtVersion(RoomUser actor, int version, bool correction)
    {
        var state = actor.Movement;
        state.BoundVersion = version;
        if (state.CurrentRef is not { } current) return;
        var oldZ = actor.Z;
        var sit = actor.Statusses.GetValueOrDefault("sit");
        var lay = actor.Statusses.GetValueOrDefault("lay");
        var rotation = (actor.RotHead, actor.RotBody);
        if (Grid.Active(current.Tile))
        {
            state.CurrentRef = Grid.Reference(current.Tile); state.SupportZ = Grid.WalkZ[current.Tile];
        }
        else { state.CurrentRef = null; state.SupportZ = oldZ; }
        PostureService.Apply(room, Grid, actor);
        if (correction || actor.Z != oldZ || sit != actor.Statusses.GetValueOrDefault("sit")
            || lay != actor.Statusses.GetValueOrDefault("lay") || rotation != (actor.RotHead, actor.RotBody))
            actor.UpdateNeeded = true;
    }
}
