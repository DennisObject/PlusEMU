namespace Plus.HabboHotel.Rooms.PathFinding;

// Runs between compilation and Version publication. No arrival or game hooks run here.
internal sealed class GeometryPublicationService(MovementContext context, RebindService rebind, RouteFallbackService fallback)
{
    private NavGrid Grid => context.Grid;

    internal void BeforePublish(IReadOnlySet<int> tiles)
    {
        context.Claims.RemapSlots(Grid.ReleasedSlots, LiveSlot);

        foreach (var actor in context.Room.GetRoomUserManager().GetUserList()) {
            var state = actor.Movement;

            if (state.State != NavState.Active) {
                continue;
            }

            var current = Grid.InBounds(actor.X, actor.Y) && tiles.Contains(Grid.Tile(actor.X, actor.Y));
            var pending = PendingTouches(state, tiles);
            var invalidated = state.HasIntent && RouteTouches(actor, tiles);

            if (current) {
                rebind.BeforePublish(actor);
            }

            var removed = ReleaseRemovedTargets(actor, tiles);

            if (current || pending) {
                context.RefreshMembership(actor);
            }

            if (invalidated || removed) {
                Invalidate(actor);
            }
        }
    }

    private static bool PendingTouches(ActorMovementState state, IReadOnlySet<int> tiles)
    {
        for (var index = 0; index < state.PendingCount; index++) {
            if (tiles.Contains(state.Pending[index].Tile)) {
                return true;
            }
        }

        return false;
    }

    private bool ReleaseRemovedTargets(RoomUser actor, IReadOnlySet<int> tiles)
    {
        var state = actor.Movement;

        for (var index = 0; index < state.PendingCount; index++) {
            var target = state.Pending[index];
            var privileged = state.Profile.LegacyOverride || state.Origin == MoveOrigin.Interaction;
            var slot = context.Graph.Slot(target, state.PendingView);

            if (!tiles.Contains(target.Tile) || slot >= 0 && (Grid.Active(slot) || privileged) || Retarget(actor, index)) {
                continue;
            }

            context.Claims.ReleaseBatch(actor);
            state.PendingCount = 0;
            actor.SetStep = false;
            actor.RemoveStatus("mv");
            actor.UpdateNeeded = true;

            return true;
        }

        return false;
    }

    // The announced surface was rebuilt away (furniture moved or changed height) but its tile may still stand:
    // the step keeps its claim on the surface it would land on, and the commit re-validates it as usual.
    private bool Retarget(RoomUser actor, int index)
    {
        var state = actor.Movement;
        var target = state.Pending[index];
        var landing = context.Graph.ResolveLanding(target, state.PendingView, context.Graph.Position(target, state.PendingView).Z);

        if (landing.Support is not { } replacement) {
            return false;
        }

        var slot = landing.Position.Slot;
        var purpose = state.PendingPurpose[index];
        var mask = ClaimMatrix.BlockingMask(state.Profile, Grid.Flags[slot], purpose, OccupancyView.Execution);

        if (!context.Claims.TryClaim(actor, slot, ClaimMatrix.KindFor(state.Profile, Grid.Flags[slot], purpose), mask)) {
            return false;
        }

        state.Pending[index] = replacement;

        return true;
    }

    private int LiveSlot(SurfaceRef surface)
    {
        var slot = Grid.SlotOf(surface);

        return slot >= 0 && Grid.Active(slot) ? slot : -1;
    }

    private bool RouteTouches(RoomUser actor, IReadOnlySet<int> tiles)
    {
        var state = actor.Movement;

        // A queued layered goal names surfaces on its tile; rebuilding that tile re-resolves it.
        if (Grid.Layered && state.AcceptedGoal is { } goal && Grid.InBounds(goal.X, goal.Y)
            && tiles.Contains(Grid.Tile(goal.X, goal.Y))) {
            return true;
        }

        if (Grid.InBounds(actor.X, actor.Y) && Near(Grid.Tile(actor.X, actor.Y), tiles)) {
            return true;
        }

        for (var index = 0; index < state.PendingCount; index++) {
            if (Near(state.Pending[index].Tile, tiles)) {
                return true;
            }
        }

        var previous = Grid.InBounds(actor.X, actor.Y) ? Grid.Tile(actor.X, actor.Y) : -1;

        for (var index = state.Cursor; index < state.Route.Count; index++) {
            var tile = state.Route.Steps[index].Tile;

            if (Near(tile, tiles) || FlanksTouch(previous, tile, tiles)) {
                return true;
            }

            previous = tile;
        }

        return false;
    }

    private bool FlanksTouch(int from, int to, IReadOnlySet<int> tiles)
    {
        if (from < 0 || from % Grid.Width == to % Grid.Width || from / Grid.Width == to / Grid.Width) {
            return false;
        }

        return Near(Grid.Tile(to % Grid.Width, from / Grid.Width), tiles)
            || Near(Grid.Tile(from % Grid.Width, to / Grid.Width), tiles);
    }

    private bool Near(int tile, IReadOnlySet<int> tiles)
    {
        var x = tile % Grid.Width;
        var y = tile / Grid.Width;

        for (var dy = -1; dy <= 1; dy++) {
            for (var dx = -1; dx <= 1; dx++) {
                if (Grid.InBounds(x + dx, y + dy) && tiles.Contains(Grid.Tile(x + dx, y + dy))) {
                    return true;
                }
            }
        }

        return false;
    }

    private void Invalidate(RoomUser actor)
    {
        var state = actor.Movement;
        state.RouteInvalidated = true;
        fallback.OnRouteInvalidated(actor);

        if (!state.LandingInProgress) {
            FinishInvalidation(actor);
        }
    }

    internal bool FinishInvalidation(RoomUser actor)
    {
        var state = actor.Movement;

        if (!state.RouteInvalidated || state.LandingInProgress || state.PendingCount != 0) {
            return false;
        }

        state.RouteInvalidated = false;

        if (state.State == NavState.Active) {
            fallback.RecomputePrefix(actor);
        }

        return true;
    }
}
