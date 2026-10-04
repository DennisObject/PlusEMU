namespace Plus.HabboHotel.Rooms.PathFinding;

public enum PathOutcome { Found, AlreadyThere, InvalidGoal, Unreachable, BudgetCancelled }
// Complete searches (blocked-route fallback) ignore the operator per-search cap.
public readonly record struct SearchRequest(ActorProfile Actor, NavPosition Start, int GoalX, int GoalY,
    PlanningOccupancy? Occupancy = null, AcceptedGoal? Goals = null, bool Complete = false);

public sealed class PathSearch(NavGrid grid, PathfindingSettings settings)
{
    private readonly MovementRules _rules = new(grid, settings);
    public PathOutcome Find(in SearchRequest req, PathWorkspace ws, Route into)
    {
        into.Clear(); ws.Begin();
        if (!grid.InBounds(req.GoalX, req.GoalY)) return PathOutcome.InvalidGoal;
        var legacy = req.Actor.LegacyOverride;
        into.View = legacy ? GraphView.LegacyTile : GraphView.Surface;
        EnsureWorkspaceFits(ws, legacy);
        var goal = req.Goals ?? GoalResolver.Resolve(grid, req.Actor, req.GoalX, req.GoalY, req.Occupancy);
        var startSlot = StartSlot(req.Start, legacy);
        if (Precheck(req.Actor, goal, startSlot, legacy) is { } early) return early;
        return new PathSearchRun(grid, _rules, settings, req, ws, goal).Execute(startSlot, into);
    }

    private void EnsureWorkspaceFits(PathWorkspace ws, bool legacy)
    {
        var address = legacy ? grid.TileCount : grid.SlotCapacity;
        var nodes = legacy ? address : grid.ActiveNodeCount;
        if (ws.AddressRange < address || ws.NodeCapacity < nodes + 1) throw new ArgumentException("Workspace does not fit the graph.");
    }

    // The start surface is the tile's surface at exactly the actor's Z; anything else is a virtual start.
    private int StartSlot(in NavPosition start, bool legacy)
    {
        if (!grid.InBounds(start.X, start.Y)) return -1;
        var tile = grid.Tile(start.X, start.Y);
        if (legacy) return tile;
        for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++)
        {
            var slot = grid.SurfaceAt(tile, ordinal);
            if (grid.Active(slot) && grid.WalkZ[slot] == start.Z) return slot;
        }
        return -1;
    }

    private PathOutcome? Precheck(ActorProfile actor, in AcceptedGoal goal, int startSlot, bool legacy)
    {
        if (startSlot >= 0 && goal.Contains(startSlot)) return PathOutcome.AlreadyThere;
        if (goal.Slot < 0) return PathOutcome.InvalidGoal;
        if (legacy || startSlot < 0) return null;
        if (!grid.Connectivity.SameComponent(startSlot, goal)) return PathOutcome.Unreachable;
        return HasWayIn(actor, goal) ? null : PathOutcome.Unreachable;
    }

    private bool HasWayIn(ActorProfile actor, in AcceptedGoal goal)
    {
        foreach (var (dx, dy) in PathTieBreak.Neighbours)
        {
            if (!actor.DiagonalEnabled && dx != 0 && dy != 0) continue;
            var x = goal.X + dx; var y = goal.Y + dy;
            if (!grid.InBounds(x, y)) continue;
            var tile = grid.Tile(x, y);
            for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++)
            {
                var slot = grid.SurfaceAt(tile, ordinal);
                if (grid.Active(slot) && StepsIntoGoal(actor, slot, goal)) return true;
            }
        }
        return false;
    }

    private bool StepsIntoGoal(ActorProfile actor, int slot, in AcceptedGoal goal)
    {
        for (var index = 0; index < goal.Count; index++)
            if (_rules.CanStep(actor, grid.Position(slot), grid.Position(goal[index]), StepPurpose.Goal, OccupancyView.Planning).Ok) return true;
        return false;
    }
}
