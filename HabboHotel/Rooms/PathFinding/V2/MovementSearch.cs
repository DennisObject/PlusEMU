namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementSearch(Room room, RoomNavigation navigation, MovementContext context,
    MovementCancellation cancellation, RouteFallbackService fallback)
{
    private SearchScheduler<RoomUser> _scheduler => context.Scheduler;
    private readonly PathSearch _search = new(navigation.Grid, navigation.Settings);
    private readonly NearestGoalSearch _nearest = new(navigation.Grid, navigation.Settings);
    // A fallback search never overwrites the walk route; Found installs it, anything else truncates.
    private readonly Route _fallbackRoute = new();
    public void Remove(RoomUser actor) => _scheduler.Remove(actor);
    public void Enqueue(RoomUser actor)
    {
        var state = actor.Movement;
        if (!state.HasIntent) return;
        _scheduler.Enqueue(actor, state.LifetimeId, state.GoalRevision);
    }
    public void Run() => _scheduler.Run(navigation.Settings.MaxExpansionsPerRoomTick, Current, Search, Complete);
    private bool Current(SearchJob<RoomUser> job)
    {
        var state = job.Actor.Movement;
        return state.State == NavState.Active && state.LifetimeId == job.LifetimeId && state.GoalRevision == job.GoalRevision && state.HasIntent;
    }
    private SearchResult Search(SearchJob<RoomUser> job)
    {
        var actor = job.Actor; var state = actor.Movement;
        var complete = fallback.OwnsSearch(actor);
        var into = complete ? _fallbackRoute : state.Route;
        var profile = context.Profiles.Refresh(actor);
        var occupancy = context.Occupancy(actor);
        var start = new NavPosition(actor.X, actor.Y, state.SupportZ);
        var goal = state.AcceptedGoal ?? InteractionGoal(actor, start) ?? GoalResolver.ResolveClick(navigation.Grid, profile, start, actor.GoalX, actor.GoalY, occupancy);
        actor.GoalX = goal.X; actor.GoalY = goal.Y;
        using var lease = PathWorkspacePool.Rent(navigation.Grid.SlotCapacity,
            profile.LegacyOverride ? navigation.Grid.SlotCapacity : navigation.Grid.ActiveNodeCount);
        var outcome = state.Origin == MoveOrigin.Interaction && goal.Slot >= 0
            ? InteractionRoute(actor, start, goal, into)
            : _search.Find(new(profile, start, goal.X, goal.Y, occupancy, goal, complete), lease.Workspace, into);
        if (complete) return new(outcome, lease.Workspace.Expansions);
        return ResolveNearest(actor, start, profile, occupancy, lease.Workspace, outcome);
    }
    private SearchResult ResolveNearest(RoomUser actor, NavPosition start, ActorProfile profile,
        PlanningOccupancy occupancy, PathWorkspace workspace, PathOutcome outcome)
    {
        var state = actor.Movement;
        var expansions = workspace.Expansions;
        if (navigation.Settings.UnreachablePolicy == "nearest" && outcome is PathOutcome.InvalidGoal or PathOutcome.Unreachable)
        {
            outcome = _nearest.Find(new(profile, start, actor.GoalX, actor.GoalY, occupancy), workspace, state.Route);
            expansions += workspace.Expansions;
            if (state.Route.GoalSurface is { } target)
            { actor.GoalX = target.Tile % navigation.Grid.Width; actor.GoalY = target.Tile / navigation.Grid.Width; }
        }
        return new(outcome, expansions);
    }
    private AcceptedGoal? InteractionGoal(RoomUser actor, NavPosition start)
    {
        if (actor.Movement.Origin != MoveOrigin.Interaction || !navigation.Grid.InBounds(actor.GoalX, actor.GoalY)) return null;
        return new(actor.GoalX, actor.GoalY, navigation.Grid.Tile(actor.GoalX, actor.GoalY));
    }
    private PathOutcome InteractionRoute(RoomUser actor, NavPosition start, AcceptedGoal goal, Route route)
    {
        var target = navigation.Grid.Position(goal.Slot);
        if (!new MovementRules(navigation.Grid, navigation.Settings).CanStep(actor.Movement.Profile,
            start, target, StepPurpose.Interaction, OccupancyView.Execution).Ok) return PathOutcome.InvalidGoal;
        route.Count = 1; route.GridVersion = navigation.Grid.Version; route.View = GraphView.Surface;
        route.Set(0, navigation.Grid.Reference(goal.Slot)); route.GoalSurface = navigation.Grid.Reference(goal.Slot);
        return PathOutcome.Found;
    }
    private void Complete(SearchJob<RoomUser> job, SearchResult result)
    {
        var actor = job.Actor;
        if (result.Outcome == PathOutcome.Found) { Install(actor); return; }
        if (result.Outcome != PathOutcome.AlreadyThere && fallback.OwnsSearch(actor)) { fallback.Truncate(actor); return; }
        cancellation.Cancel(actor);
        if (result.Outcome == PathOutcome.AlreadyThere) PostureService.Apply(room, navigation.Grid, actor);
    }
    private void Install(RoomUser actor)
    {
        if (fallback.OwnsSearch(actor)) fallback.Reroute(actor, _fallbackRoute);
        else actor.Movement.Route.CaptureAdvisory(navigation.Grid);
        actor.IsWalking = true;
    }
}
