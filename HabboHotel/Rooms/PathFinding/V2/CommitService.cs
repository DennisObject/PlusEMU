namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class CommitService(Room room, RoomNavigation navigation, MovementContext context,
    MovementCancellation cancellation, RouteFallbackService fallback, ApproachCompletion approaches)
{
    private readonly MovementRules _rules = new(navigation.Grid, navigation.Settings);
    private readonly LandingService _landing = new(room, navigation, context, cancellation);
    public bool Commit(RoomUser actor)
    {
        var state = actor.Movement;
        if (state.PendingCount == 0) { context.Geometry.FinishInvalidation(actor); return false; }
        if (ReplacedSeatGoal(actor)) { cancellation.Cancel(actor); return false; }
        var revision = state.LocationRevision;
        var count = state.PendingCount;
        var accepted = ValidPrefix(actor);
        state.PendingCount = 0; actor.SetStep = false;
        if (accepted > 0) LandPending(actor, accepted);
        context.Claims.ReleaseBatch(actor);
        if (state.State != NavState.Active || state.LocationRevision != revision) return accepted > 0;
        if (accepted > 0) state.WaitTicks = state.BlockReplans = state.StallTicks = 0;
        var arrived = Advance(actor, accepted, count);
        context.Geometry.FinishInvalidation(actor);
        if (arrived) approaches.Complete(actor, revision);
        return accepted > 0;
    }
    private void LandPending(RoomUser actor, int accepted)
    {
        actor.Movement.LandingInProgress = true;
        try { _landing.Land(actor, actor.Movement.Pending[accepted - 1], actor.Movement.PendingView); }
        finally { actor.Movement.LandingInProgress = false; }
    }
    private bool ReplacedSeatGoal(RoomUser actor)
    {
        var state = actor.Movement;
        var target = state.Pending[state.PendingCount - 1];
        var command = state.Commands.Read();
        return context.Graph.IsSeat(target.Tile) && state.Route.GoalSurface == target
            && command != null && command.Sequence > state.ConsumedSequence && !actor.Frozen
            && (actor.CanWalk || command.Origin != MoveOrigin.User);
    }
    // True when the committed prefix reached the end of the route.
    private bool Advance(RoomUser actor, int accepted, int count)
    {
        var state = actor.Movement;
        state.Cursor += accepted;
        if (accepted != count) { fallback.OnRouteBlocked(actor); return false; }
        if (state.Cursor < state.Route.Count || !fallback.RouteFinished(state)) return false;
        cancellation.Finish(actor);
        return true;
    }
    private int ValidPrefix(RoomUser actor)
    {
        var state = actor.Movement;
        var profile = context.Profiles.Refresh(actor);
        var from = new NavPosition(actor.X, actor.Y, state.SupportZ);
        var accepted = 0;
        for (var i = 0; i < state.PendingCount; i++)
        {
            var step = state.Pending[i];
            if (!context.Graph.IsValid(step, state.PendingView)) break;
            var to = context.Graph.Position(step.Tile, state.PendingView);
            var purpose = state.Origin == MoveOrigin.Interaction ? StepPurpose.Interaction
                : to.X == actor.GoalX && to.Y == actor.GoalY ? StepPurpose.Goal : StepPurpose.Transit;
            if (!_rules.CanStep(profile, from, to, purpose, OccupancyView.Execution, context.OccupancyAt(actor, step.Tile)).Ok) break;
            context.GuildGates.Accept(actor, profile, step.Tile, purpose);
            accepted++; from = to;
        }
        return accepted;
    }
}
