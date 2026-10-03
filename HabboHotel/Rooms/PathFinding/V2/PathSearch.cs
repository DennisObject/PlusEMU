namespace Plus.HabboHotel.Rooms.PathFinding;

public enum PathOutcome { Found, AlreadyThere, InvalidGoal, Unreachable, BudgetCancelled }
public readonly record struct SearchRequest(ActorProfile Actor, NavPosition Start, int GoalX, int GoalY,
    PlanningOccupancy? Occupancy = null, AcceptedGoal? Goals = null);

public sealed class PathSearch(NavGrid grid, PathfindingSettings settings)
{
    private readonly MovementRules _rules = new(grid, settings);
    public PathOutcome Find(in SearchRequest req, PathWorkspace ws, Route into)
    {
        into.Clear(); ws.Begin();
        if (!grid.InBounds(req.GoalX, req.GoalY)) return PathOutcome.InvalidGoal;
        var legacy = req.Actor.LegacyOverride;
        var address = legacy ? grid.Width * grid.Height : grid.SlotCapacity;
        var nodes = legacy ? address : grid.ActiveNodeCount;
        if (ws.AddressRange < address || ws.NodeCapacity < nodes + 1) throw new ArgumentException("Workspace does not fit the graph.");
        var goal = req.Goals ?? GoalResolver.Resolve(grid, req.Actor, req.GoalX, req.GoalY, req.Occupancy);
        var start = req.Start;
        var startSlot = grid.InBounds(start.X, start.Y) ? grid.Tile(start.X, start.Y) : -1;
        var virtualStart = startSlot < 0 || !legacy && (!grid.Active(startSlot) || grid.WalkZ[startSlot] != start.Z);
        if (!virtualStart && goal.Contains(startSlot)) return PathOutcome.AlreadyThere;
        if (goal.Slot < 0) return PathOutcome.InvalidGoal;
        if (!legacy && !virtualStart)
        {
            if (!grid.Connectivity.SameComponent(startSlot, goal.Slot)) return PathOutcome.Unreachable;
            if (!HasWayIn(req.Actor, goal)) return PathOutcome.Unreachable;
        }
        var first = virtualStart ? address : startSlot;
        ws.Stamp[first] = ws.Generation; ws.G[first] = 0; ws.Parent[first] = -1;
        ws.Sequence[first] = ws.NextSequence++;
        ws.Insert(first, PathTieBreak.Key(PathTieBreak.Heuristic(start.X, start.Y, goal.X, goal.Y),
            start.X, start.Y, goal.X, goal.Y, 0, ws.Sequence[first]));
        var cap = settings.MaxExpansionsPerSearch ?? nodes + 1;
        while (ws.Count > 0)
        {
            var current = ws.Pop();
            if (goal.Contains(current))
            {
                Reconstruct(current, first, legacy, ws, into);
                return PathOutcome.Found;
            }
            if (ws.Expansions >= cap) return PathOutcome.BudgetCancelled;
            ws.Expansions++;
            ws.Stamp[current] = -ws.Generation;
            var from = current == address ? start : grid.Position(current, legacy);
            foreach (var (dx, dy) in PathTieBreak.Neighbours)
            {
                if (!req.Actor.DiagonalEnabled && dx != 0 && dy != 0) continue;
                var x = from.X + dx; var y = from.Y + dy;
                if (!grid.InBounds(x, y)) continue;
                var next = grid.Tile(x, y);
                if (!legacy && !grid.Active(next) || ws.Stamp[next] == -ws.Generation) continue;
                var purpose = goal.Contains(next) ? StepPurpose.Goal : StepPurpose.Transit;
                ws.CanStepCalls++;
                if (!_rules.CanStep(req.Actor, from, grid.Position(next, legacy), purpose, OccupancyView.Planning, req.Occupancy).Ok) continue;
                var g = ws.G[current] + 1;
                var seen = ws.Stamp[next] == ws.Generation;
                if (seen && g >= ws.G[next]) continue;
                ws.G[next] = g; ws.Parent[next] = current;
                if (!seen) { ws.Stamp[next] = ws.Generation; ws.Sequence[next] = ws.NextSequence++; }
                var key = PathTieBreak.Key(g + PathTieBreak.Heuristic(x, y, goal.X, goal.Y), x, y,
                    goal.X, goal.Y, legacy ? (byte)0 : grid.Ordinal[next], ws.Sequence[next]);
                if (seen) ws.Decrease(next, key); else ws.Insert(next, key);
            }
        }
        return PathOutcome.Unreachable;
    }

    private bool HasWayIn(ActorProfile actor, AcceptedGoal goal)
    {
        foreach (var (dx, dy) in PathTieBreak.Neighbours)
        {
            if (!actor.DiagonalEnabled && dx != 0 && dy != 0) continue;
            var x = goal.X + dx; var y = goal.Y + dy;
            if (!grid.InBounds(x, y)) continue;
            var slot = grid.Tile(x, y);
            if (grid.Active(slot) && _rules.CanStep(actor, grid.Position(slot), grid.Position(goal.Slot), StepPurpose.Goal, OccupancyView.Planning).Ok) return true;
        }
        return false;
    }

    private void Reconstruct(int goal, int start, bool legacy, PathWorkspace ws, Route into)
    {
        var count = 0;
        for (var n = goal; n != start; n = ws.Parent[n]) count++;
        into.EnsureCapacity(count); into.Count = count; into.GridVersion = grid.Version;
        for (var n = goal; n != start; n = ws.Parent[n])
            into.Set(--count, legacy ? new(n, 0, SurfaceKind.Floor) : grid.Reference(n));
        into.GoalSurface = legacy ? new(goal, 0, SurfaceKind.Floor) : grid.Reference(goal);
    }
}
