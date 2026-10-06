namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class RebindService(Room room, NavGrid Grid)
{
    public void Rebind(RoomUser actor)
    {
        var state = actor.Movement;

        if (state.BoundVersion == Grid.Version) {
            return;
        }

        // Dirty-tile publication already rebound affected actors before Version changed.
        state.BoundVersion = Grid.Version;
    }

    internal void BeforePublish(RoomUser actor) => RebindAtVersion(actor, Grid.Version + 1, true);

    private void RebindAtVersion(RoomUser actor, int version, bool correction)
    {
        var state = actor.Movement;
        state.BoundVersion = version;

        if (state.CurrentRef is not { } current) {
            return;
        }

        var oldZ = actor.Z;
        var sit = actor.Statusses.GetValueOrDefault("sit");
        var lay = actor.Statusses.GetValueOrDefault("lay");
        var rotation = (actor.RotHead, actor.RotBody);
        var slot = SupportAfterPublish(current, state.SupportZ);

        if (slot >= 0) {
            state.CurrentRef = Grid.Reference(slot);
            state.SupportZ = Grid.WalkZ[slot];
        }
        else {
            state.CurrentRef = null;
            state.SupportZ = oldZ;
        }

        PostureService.Apply(room, Grid, actor);

        if (correction || actor.Z != oldZ || sit != actor.Statusses.GetValueOrDefault("sit")
            || lay != actor.Statusses.GetValueOrDefault("lay") || rotation != (actor.RotHead, actor.RotBody)) {
            actor.UpdateNeeded = true;
        }
    }

    // §5.3 step 2: a surviving support keeps its slot; otherwise land on the highest surface at or
    // below the old Z, else the highest. Surfaces dropped by the pinned overflow cap go off-graph.
    private int SupportAfterPublish(SurfaceRef current, double supportZ)
    {
        if (!Grid.Layered) {
            return Grid.Active(current.Tile) ? current.Tile : -1;
        }

        if (Grid.ForcedOffGraph.Contains(current)) {
            return -1;
        }

        var slot = Grid.SlotOf(current);

        if (slot >= 0 && Grid.Active(slot)) {
            return slot;
        }

        var below = SurfaceSelection.Select(Grid, current.Tile, supportZ, ForceResolution.NearestAtOrBelow);

        return below >= 0 ? below : SurfaceSelection.Select(Grid, current.Tile, supportZ, ForceResolution.Highest);
    }
}
