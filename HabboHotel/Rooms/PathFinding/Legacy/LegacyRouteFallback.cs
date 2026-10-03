namespace Plus.HabboHotel.Rooms.PathFinding;

// Spec 16.11 for the legacy engine. Every failure of the actor's own route lands here before
// anything destructive happens: a walking-only blocker is waited for first (6.5), then a
// complete (uncapped) recalc reroutes, otherwise the valid prefix of the unconsumed path is
// installed. Truncated routes never search again.
internal sealed class LegacyRouteFallback(PathfindingSettings settings)
{
    private readonly ValidPrefixFinder _finder = new();

    // Returns false when the actor must stop now.
    public bool OnBlocked(Gamemap map, RoomUser user)
    {
        var state = user.Movement;
        if (Eligible(user) && ++state.StallTicks >= settings.MaxWalkStallTicks) return false;
        state.WaitTicks++;
        if (BlockedOnlyByWalkers(map, user) && state.WaitTicks <= settings.BlockWaitTicks) return true;
        var retained = Retain(map, user);
        if (state.Fallback.Suspect(retained, 0))
        {
            if (Reroute(map, user)) { state.Fallback.Found(); return true; }
            state.Fallback.Truncate(int.MaxValue);
        }
        return InstallPrefix(map, user, retained);
    }

    public static void OnStepCommitted(RoomUser user) => user.Movement.StallTicks = user.Movement.WaitTicks = 0;

    // Deliberate suppression (freeze game, interaction holds) suspends and resets the stall counter.
    public static void OnSuppressed(RoomUser user) => user.Movement.StallTicks = 0;

    public static bool Eligible(RoomUser user) => !user.Freezed && user.CanWalk;

    public static void OnCommand(RoomUser user)
    {
        user.Movement.Fallback.Begin(user.Movement.NextSequence());
        OnStepCommitted(user);
    }

    // The next edge is valid once walking users and their tile marks are ignored.
    private static bool BlockedOnlyByWalkers(Gamemap map, RoomUser user)
    {
        if (LegacyRoutePath.Remaining(user) is not [var next, ..]) return false;
        var walkers = map.GetRoomUsers(new(next.X, next.Y)).Any(other => !ReferenceEquals(other, user) && other.IsWalking);
        return walkers && map.IsValidStepPure(user, new(user.X, user.Y), next, next.X == user.GoalX && next.Y == user.GoalY,
            user.AllowOverride, LegacyStepView.Prefix).Ok;
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
