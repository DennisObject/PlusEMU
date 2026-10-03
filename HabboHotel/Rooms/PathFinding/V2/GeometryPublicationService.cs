namespace Plus.HabboHotel.Rooms.PathFinding;

// Runs between compilation and Version publication. No arrival or game hooks run here.
internal sealed class GeometryPublicationService(MovementContext context, RebindService rebind)
{
    private NavGrid Grid => context.Grid;

    internal void BeforePublish(IReadOnlySet<int> tiles)
    {
        foreach (var actor in context.Room.GetRoomUserManager().GetUserList())
        {
            var state = actor.Movement;
            if (state.State != NavState.Active) continue;
            var current = Grid.InBounds(actor.X, actor.Y) && tiles.Contains(Grid.Tile(actor.X, actor.Y));
            var pending = PendingTouches(state, tiles);
            var invalidated = state.HasIntent && RouteTouches(actor, tiles);
            if (current) rebind.BeforePublish(actor);
            var removed = ReleaseRemovedTargets(actor, tiles);
            if (current || pending) context.RefreshMembership(actor);
            if (invalidated || removed) Invalidate(actor);
        }
    }

    private static bool PendingTouches(ActorMovementState state, IReadOnlySet<int> tiles)
    {
        for (var index = 0; index < state.PendingCount; index++)
            if (tiles.Contains(state.Pending[index].Tile)) return true;
        return false;
    }

    private bool ReleaseRemovedTargets(RoomUser actor, IReadOnlySet<int> tiles)
    {
        var state = actor.Movement;
        for (var index = 0; index < state.PendingCount; index++)
        {
            var target = state.Pending[index];
            var privileged = state.Profile.LegacyOverride || state.Origin == MoveOrigin.Interaction;
            if (!tiles.Contains(target.Tile)
                || (Grid.Active(target.Tile) || privileged) && Grid.Reference(target.Tile) == target) continue;
            context.Claims.ReleaseBatch(actor);
            state.PendingCount = 0; actor.SetStep = false;
            actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
            return true;
        }
        return false;
    }

    private bool RouteTouches(RoomUser actor, IReadOnlySet<int> tiles)
    {
        var state = actor.Movement;
        if (Grid.InBounds(actor.X, actor.Y) && Near(Grid.Tile(actor.X, actor.Y), tiles)) return true;
        for (var index = 0; index < state.PendingCount; index++)
            if (Near(state.Pending[index].Tile, tiles)) return true;
        var previous = Grid.InBounds(actor.X, actor.Y) ? Grid.Tile(actor.X, actor.Y) : -1;
        for (var index = state.Cursor; index < state.Route.Count; index++)
        {
            var tile = state.Route.Steps[index].Tile;
            if (Near(tile, tiles) || FlanksTouch(previous, tile, tiles)) return true;
            previous = tile;
        }
        return false;
    }

    private bool FlanksTouch(int from, int to, IReadOnlySet<int> tiles)
    {
        if (from < 0 || from % Grid.Width == to % Grid.Width || from / Grid.Width == to / Grid.Width) return false;
        return Near(Grid.Tile(to % Grid.Width, from / Grid.Width), tiles)
            || Near(Grid.Tile(from % Grid.Width, to / Grid.Width), tiles);
    }

    private bool Near(int tile, IReadOnlySet<int> tiles)
    {
        var x = tile % Grid.Width; var y = tile / Grid.Width;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (Grid.InBounds(x + dx, y + dy) && tiles.Contains(Grid.Tile(x + dx, y + dy))) return true;
        return false;
    }

    private void Invalidate(RoomUser actor)
    {
        var state = actor.Movement;
        state.GoalRevision++; state.RouteInvalidated = true; state.AcceptedGoal = null;
        if (state.LandingInProgress) return;
        if (state.PendingCount == 0) FinishInvalidation(actor);
        else context.Replan(actor);
    }

    internal bool FinishInvalidation(RoomUser actor)
    {
        var state = actor.Movement;
        if (!state.RouteInvalidated || state.LandingInProgress || state.PendingCount != 0) return false;
        state.RouteInvalidated = false; state.Route.Clear(); state.Cursor = 0;
        if (state.State == NavState.Active) context.Replan(actor);
        return true;
    }
}
