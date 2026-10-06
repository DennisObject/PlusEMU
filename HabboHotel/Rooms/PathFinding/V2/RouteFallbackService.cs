namespace Plus.HabboHotel.Rooms.PathFinding;

// Spec 16.11: a blocked route of the same command takes a complete replan's alternative,
// otherwise walks its longest valid prefix. Route states take precedence over 5.3 step 4 and 6.5.
internal sealed class RouteFallbackService(RoomNavigation navigation, MovementContext context,
    MovementCancellation cancellation)
{
    private readonly ValidPrefixFinder _finder = new();
    private readonly MovementRules _rules = new(navigation.Grid, navigation.Settings);
    private NavGrid Grid => navigation.Grid;

    // Non-transient announce failure, or a partial or zero-prefix commit.
    public void OnRouteBlocked(RoomUser actor)
    {
        actor.RemoveStatus("mv");
        actor.UpdateNeeded = true;
        Suspect(actor);
        RecomputePrefix(actor);
    }

    // A geometry publication touched the route; the prefix follows once no batch is pending.
    public void OnRouteInvalidated(RoomUser actor)
    {
        var state = actor.Movement;

        if (state.Fallback.State != RouteState.Normal)
        {
            return;
        }

        if (state.Cursor < state.Route.Count)
        {
            Suspect(actor);

            return;
        }

        // No route installed yet: the command's own search is simply replaced.
        state.GoalRevision++;
        state.AcceptedGoal = null;
        context.Replan(actor);
    }

    public void RecomputePrefix(RoomUser actor)
    {
        var state = actor.Movement;
        var machine = state.Fallback;

        if (machine.State == RouteState.Normal)
        {
            return;
        }

        machine.Consume(state.Cursor);
        var prefix = _finder.Find(Start(actor), machine.Retained, Graph(actor));
        machine.Shorten(prefix.Length);
        RouteRetention.Install(state, prefix, machine.Retained, Grid);
        machine.Rebind();

        if (machine.State == RouteState.Truncated && prefix.Length == 0)
        {
            cancellation.Cancel(actor);
        }
    }

    public bool OwnsSearch(RoomUser actor) => actor.Movement.Fallback.State == RouteState.Suspect;

    public void Reroute(RoomUser actor, Route found)
    {
        var state = actor.Movement;
        state.Fallback.Found();
        state.Route.CopyFrom(found);
        state.Route.CaptureAdvisory(Grid);
        state.Cursor = 0;
    }

    // Only completed fallback searches that failed count towards max_block_replans.
    public void Truncate(RoomUser actor)
    {
        var state = actor.Movement;
        state.BlockReplans++;
        state.Fallback.Truncate(int.MaxValue);
        RecomputePrefix(actor);
    }

    // Waiting at the end of the safe prefix for a fallback search that has not started.
    public bool AwaitsUnstartedSearch(RoomUser actor)
    {
        var state = actor.Movement;

        return state.Fallback.State == RouteState.Suspect && state.PendingCount == 0
            && state.Cursor >= state.Route.Count && context.Scheduler.Contains(actor);
    }

    // Arrival at the end of the walk route stops the actor unless a fallback search may extend it.
    public bool RouteFinished(ActorMovementState state)
        => state.Fallback.State != RouteState.Suspect || state.Route.GoalSurface != null;

    private void Suspect(RoomUser actor)
    {
        var state = actor.Movement;

        if (!state.Fallback.Suspect(RouteRetention.Capture(state, Grid), state.Cursor))
        {
            return;
        }

        state.GoalRevision++;
        state.AcceptedGoal = null;
        context.Scheduler.Enqueue(actor, state.LifetimeId, state.GoalRevision);
    }

    private PrefixCandidate Start(RoomUser actor)
    {
        var current = actor.Movement.CurrentRef;
        var slot = current is not { } surface ? -1 : Grid.Layered ? Grid.SlotOf(surface) : surface.Tile;

        return new(actor.X, actor.Y, actor.Movement.SupportZ, current?.SupportItemId ?? 0, slot);
    }

    private NavPrefixGraph Graph(RoomUser actor)
    {
        var occupancy = context.Occupancy(actor);

        for (var slot = 0; slot < occupancy.Targets.Length; slot++)
        {
            occupancy.Targets[slot] &= NavPrefixGraph.Kept;
        }

        return new(Grid, _rules, context.Profiles.Refresh(actor), occupancy);
    }
}
