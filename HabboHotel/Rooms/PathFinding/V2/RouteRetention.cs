namespace Plus.HabboHotel.Rooms.PathFinding;

// Converts between the walk route (bound surfaces) and the retained XY suffix.
internal static class RouteRetention
{
    public static RetainedStep[] Capture(ActorMovementState state, NavGrid grid)
    {
        var route = state.Route;
        var steps = new RetainedStep[Math.Max(0, route.Count - state.Cursor)];
        for (var i = state.Cursor; i < route.Count; i++)
        {
            var surface = route.Steps[i];
            steps[i - state.Cursor] = new(surface.Tile % grid.Width, surface.Tile / grid.Width,
                route.PurposeAt(i, state.Origin), surface.SupportItemId, route.AdvisoryZ(i));
        }
        return steps;
    }

    // Rebinds the prefix to current surfaces; the goal edge survives only if the prefix reaches it.
    public static void Install(ActorMovementState state, PrefixCandidate[] prefix,
        IReadOnlyList<RetainedStep> retained, NavGrid grid)
    {
        var route = state.Route;
        route.EnsureCapacity(prefix.Length);
        route.Count = prefix.Length; route.GridVersion = grid.Version;
        route.View = state.Profile.LegacyOverride ? GraphView.LegacyTile : GraphView.Surface;
        for (var i = 0; i < prefix.Length; i++)
        {
            route.Set(i, grid.Reference(prefix[i].Slot));
            route.SetAdvisoryZ(i, retained[i].Z);
        }
        var reachesGoal = prefix.Length > 0 && prefix.Length == retained.Count
            && retained[^1].Purpose is StepPurpose.Goal or StepPurpose.Interaction;
        route.GoalSurface = reachesGoal ? route.Steps[^1] : null;
        state.Cursor = 0;
    }
}
