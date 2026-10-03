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
        var actor = req.Actor;
        var legacy = actor.LegacyOverride;
        into.View = legacy ? GraphView.LegacyTile : GraphView.Surface;
        var diagonal = actor.DiagonalEnabled;
        var width = grid.Width; var height = grid.Height;
        var stamps = ws.Stamp; var costs = ws.G; var parents = ws.Parent; var sequences = ws.Sequence;
        var generation = ws.Generation; var closed = -generation;
        var flags = grid.Flags; var heights = legacy ? grid.LegacyZ : grid.WalkZ;
        var address = legacy ? grid.Width * grid.Height : grid.SlotCapacity;
        var nodes = legacy ? address : grid.ActiveNodeCount;
        if (ws.AddressRange < address || ws.NodeCapacity < nodes + 1) throw new ArgumentException("Workspace does not fit the graph.");
        var goal = req.Goals ?? GoalResolver.Resolve(grid, actor, req.GoalX, req.GoalY, req.Occupancy);
        var start = req.Start;
        var startSlot = grid.InBounds(start.X, start.Y) ? grid.Tile(start.X, start.Y) : -1;
        var virtualStart = startSlot < 0 || !legacy && (!grid.Active(startSlot) || grid.WalkZ[startSlot] != start.Z);
        if (!virtualStart && goal.Contains(startSlot)) return PathOutcome.AlreadyThere;
        if (goal.Slot < 0) return PathOutcome.InvalidGoal;
        if (!legacy && !virtualStart)
        {
            if (!grid.Connectivity.SameComponent(startSlot, goal.Slot)) return PathOutcome.Unreachable;
            if (!HasWayIn(actor, goal)) return PathOutcome.Unreachable;
        }
        var first = virtualStart ? address : startSlot;
        stamps[first] = generation; costs[first] = 0; parents[first] = -1;
        sequences[first] = ws.NextSequence++;
        ws.Insert(first, PathTieBreak.Key(PathTieBreak.Heuristic(start.X, start.Y, goal.X, goal.Y),
            start.X, start.Y, goal.X, goal.Y, 0, sequences[first]));
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
            stamps[current] = -generation;
            var from = current == address ? start : new NavPosition(current % width, current / width, heights[current], current);
            if (current == address && !grid.InBounds(start.X, start.Y)) continue;
            var g = costs[current] + 1;
            foreach (var (dx, dy) in PathTieBreak.Neighbours)
            {
                if (!diagonal && dx != 0 && dy != 0) continue;
                var x = from.X + dx; var y = from.Y + dy;
                if ((uint)x >= width || (uint)y >= height) continue;
                var next = y * width + x;
                var stamp = stamps[next];
                if (stamp == closed) continue;
                var seen = stamp == generation;
                if (seen && g >= costs[next] || !legacy && (flags[next] & ~NavFlags.FloorLocked) == 0) continue;
                var purpose = goal.Contains(next) ? StepPurpose.Goal : StepPurpose.Transit;
                var to = new NavPosition(x, y, heights[next], next);
                ws.CanStepCalls++;
                if (!_rules.CanStepKnownNeighbour(actor, from, to, next, purpose, OccupancyView.Planning, req.Occupancy).Ok) continue;
                costs[next] = g; parents[next] = current;
                if (!seen) { stamps[next] = generation; sequences[next] = ws.NextSequence++; }
                var key = PathTieBreak.Key(g + PathTieBreak.Heuristic(x, y, goal.X, goal.Y), x, y,
                    goal.X, goal.Y, legacy ? (byte)0 : grid.Ordinal[next], sequences[next]);
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
