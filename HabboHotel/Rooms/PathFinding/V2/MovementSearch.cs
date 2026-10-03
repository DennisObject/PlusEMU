namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementSearch(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private SearchScheduler<RoomUser> _scheduler => context.Scheduler;
    private readonly PathSearch _search = new(navigation.Grid, navigation.Settings);
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
        var profile = MovementProfiles.Refresh(room, navigation.Grid, navigation.Settings, actor);
        var occupancy = context.Occupancy(actor);
        var start = new NavPosition(actor.X, actor.Y, state.SupportZ);
        var goal = InteractionGoal(actor, start) ?? GoalResolver.ResolveClick(navigation.Grid, profile, start, actor.GoalX, actor.GoalY, occupancy);
        actor.GoalX = goal.X; actor.GoalY = goal.Y;
        using var lease = PathWorkspacePool.Rent(navigation.Grid.SlotCapacity,
            profile.LegacyOverride ? navigation.Grid.SlotCapacity : navigation.Grid.ActiveNodeCount);
        var outcome = state.Origin == MoveOrigin.Interaction && goal.Slot >= 0
            ? InteractionRoute(actor, start, goal) : _search.Find(new(profile, start, goal.X, goal.Y, occupancy, goal), lease.Workspace, state.Route);
        return new(outcome, lease.Workspace.Expansions);
    }
    private AcceptedGoal? InteractionGoal(RoomUser actor, NavPosition start)
    {
        if (actor.Movement.Origin != MoveOrigin.Interaction || !navigation.Grid.InBounds(actor.GoalX, actor.GoalY)) return null;
        return new(actor.GoalX, actor.GoalY, navigation.Grid.Tile(actor.GoalX, actor.GoalY));
    }
    private PathOutcome InteractionRoute(RoomUser actor, NavPosition start, AcceptedGoal goal)
    {
        var target = navigation.Grid.Position(goal.Slot);
        if (!new MovementRules(navigation.Grid, navigation.Settings).CanStep(actor.Movement.Profile,
            start, target, StepPurpose.Interaction, OccupancyView.Execution).Ok) return PathOutcome.InvalidGoal;
        var route = actor.Movement.Route;
        route.Count = 1; route.GridVersion = navigation.Grid.Version;
        route.Set(0, navigation.Grid.Reference(goal.Slot)); route.GoalSurface = navigation.Grid.Reference(goal.Slot);
        return PathOutcome.Found;
    }
    private void Complete(SearchJob<RoomUser> job, SearchResult result)
    {
        var actor = job.Actor; var state = actor.Movement;
        if (result.Outcome == PathOutcome.Found) { actor.IsWalking = true; return; }
        cancellation.Cancel(actor);
        if (result.Outcome == PathOutcome.AlreadyThere) PostureService.Apply(room, navigation.Grid, actor);
    }
}
