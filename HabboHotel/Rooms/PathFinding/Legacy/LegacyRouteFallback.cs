namespace Plus.HabboHotel.Rooms.PathFinding;

// Spec 16.11 for the legacy engine. Every failure of the actor's own route lands here before
// anything destructive happens: a complete (uncapped) recalc reroutes, otherwise the valid
// prefix of the unconsumed path is installed. Truncated routes never search again.
internal sealed class LegacyRouteFallback
{
    // Mirrors the default max_walk_stall_ticks: blocked ticks without a committed step.
    public const int MaxHeldTicks = 10;
    private readonly ValidPrefixFinder _finder = new();

    // Returns false when the actor must stop now.
    public bool OnBlocked(Gamemap map, RoomUser user)
    {
        var state = user.Movement;
        if (++state.StallTicks >= MaxHeldTicks) return false;
        var retained = Retain(map, user);
        if (state.Fallback.Suspect(retained, 0))
        {
            if (Reroute(map, user)) { state.Fallback.Found(); return true; }
            state.Fallback.Truncate(int.MaxValue);
        }
        return InstallPrefix(map, user, retained);
    }

    public static void OnStepCommitted(RoomUser user) => user.Movement.StallTicks = 0;

    public static void OnCommand(RoomUser user)
    {
        user.Movement.Fallback.Begin(user.Movement.NextSequence());
        user.Movement.StallTicks = 0;
    }

    private static RetainedStep[] Retain(Gamemap map, RoomUser user) => LegacyRoutePath.Remaining(user)
        .Select(tile => new RetainedStep(tile.X, tile.Y,
            tile.X == user.GoalX && tile.Y == user.GoalY ? StepPurpose.Goal : StepPurpose.Transit,
            0, map.SqAbsoluteHeight(tile.X, tile.Y)))
        .ToArray();

    private static bool Reroute(Gamemap map, RoomUser user)
    {
        var path = PathFinder.FindPath(user, map.DiagonalEnabled, map, new(user.X, user.Y), new(user.GoalX, user.GoalY),
            (from, to, end) => map.IsValidStepPure(user, from, to, end, user.AllowOverride, LegacyStepView.Prefix).Ok);
        if (path.Count <= 1) return false;
        LegacyRoutePath.Install(user, path);
        return true;
    }

    // Truncated routes are re-derived from the installed path, so they can only shorten.
    private bool InstallPrefix(Gamemap map, RoomUser user, IReadOnlyList<RetainedStep> retained)
    {
        var start = new PrefixCandidate(user.X, user.Y, map.SqAbsoluteHeight(user.X, user.Y), 0, -1);
        var prefix = _finder.Find(start, retained, new LegacyPrefixGraph(map, user));
        user.Movement.Fallback.Shorten(prefix.Length);
        LegacyRoutePath.InstallPrefix(user, prefix.Select(c => new Vector2D(c.X, c.Y)).ToArray());
        return prefix.Length > 0;
    }
}
