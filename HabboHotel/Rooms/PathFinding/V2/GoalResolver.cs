namespace Plus.HabboHotel.Rooms.PathFinding;

// K=1 has at most one accepted node. Identity is shared by every search precheck.
public readonly record struct AcceptedGoal(int X, int Y, int Slot)
{
    public bool Contains(int slot) => Slot >= 0 && slot == Slot;
}

public static class GoalResolver
{
    // Click resolution is separate from Find: shadow searches must use legacy GoalX/Y.
    // P2 calls this at command consumption, then passes the resolved accepted identity.
    public static AcceptedGoal ResolveClick(NavGrid grid, ActorProfile actor, in NavPosition start,
        int x, int y, PlanningOccupancy? occupancy, ActorAccessResolver? access = null)
    {
        if (!grid.InBounds(x, y)) return new(x, y, -1);
        var clicked = grid.Tile(x, y);
        if ((grid.Flags[clicked] & NavFlags.GoalOnlyBed) == 0 || actor.LegacyOverride)
            return Resolve(grid, actor, x, y, occupancy, access);
        var best = new AcceptedGoal(x, y, -1);
        var distance = int.MaxValue;
        foreach (var tile in grid.PillowTiles[clicked])
        {
            if (grid.SupportItem[tile] != grid.SupportItem[clicked] || (grid.Flags[tile] & NavFlags.GoalOnlyBed) == 0) continue;
            var candidate = Resolve(grid, actor, tile % grid.Width, tile / grid.Width, occupancy, access);
            var nextDistance = PathTieBreak.Heuristic(start.X, start.Y, candidate.X, candidate.Y);
            if (candidate.Slot >= 0 && nextDistance < distance) { best = candidate; distance = nextDistance; }
        }
        return best;
    }

    public static AcceptedGoal Resolve(NavGrid grid, ActorProfile actor, int x, int y, PlanningOccupancy? occupancy,
        ActorAccessResolver? access = null)
    {
        if (!grid.InBounds(x, y)) return new(x, y, -1);
        var t = grid.Tile(x, y);
        if (actor.LegacyOverride) return new(x, y, t);
        var flags = grid.Flags[t];
        if ((flags & (NavFlags.Transit | NavFlags.GoalOnlySeat | NavFlags.GoalOnlyBed | NavFlags.Door)) == 0
            || (flags & NavFlags.FloorLocked) != 0
            || (flags & NavFlags.GuildGate) != 0 && !(access ?? ActorAccessResolver.Cached).CanEnterGuildGate(actor, grid.GroupId[t])
            || occupancy != null && (occupancy.Targets[t] & ClaimMatrix.BlockingMask(actor, flags, StepPurpose.Goal, OccupancyView.Execution)) != 0)
            return new(x, y, -1);
        return new(x, y, t);
    }
}
