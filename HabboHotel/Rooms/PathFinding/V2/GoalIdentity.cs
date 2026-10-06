namespace Plus.HabboHotel.Rooms.PathFinding;

// A goal accepted at command intake and searched later. Layered slots are reusable after a rebuild
// (§5.3), so the goal persists surface identities and maps them back to slots when the search runs.
internal readonly record struct GoalIdentity(int X, int Y, int Count, SurfaceRef S0, SurfaceRef S1, SurfaceRef S2, SurfaceRef S3)
{
    internal static GoalIdentity Capture(NavGrid grid, AcceptedGoal goal)
    {
        SurfaceRef At(int index) => index < goal.Count ? grid.Reference(goal[index]) : default;

        return new(goal.X, goal.Y, goal.Count, At(0), At(1), At(2), At(3));
    }

    // K=1 slots are tiles and never move. Null means every layered surface is gone: resolve the click again.
    internal AcceptedGoal? Resolve(NavGrid grid)
    {
        var goal = new AcceptedGoal(X, Y, -1);

        for (var index = 0; index < Count; index++)
        {
            var surface = this[index];
            var slot = grid.Layered ? grid.SlotOf(surface) : surface.Tile;

            if (slot >= 0)
            {
                goal = goal.With(slot);
            }
        }

        return Count > 0 && goal.Count == 0 ? null : goal;
    }

    private SurfaceRef this[int index] => index switch { 0 => S0, 1 => S1, 2 => S2, _ => S3 };
}
