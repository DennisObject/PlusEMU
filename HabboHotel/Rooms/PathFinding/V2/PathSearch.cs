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
        var actor = req.Actor;
        var legacy = actor.LegacyOverride;
        into.View = legacy ? GraphView.LegacyTile : GraphView.Surface;
        var diagonal = actor.DiagonalEnabled;
        var width = grid.Width; var height = grid.Height;
        var stamps = ws.Stamp; var costs = ws.G; var parents = ws.Parent; var sequences = ws.Sequence;
        var generation = ws.Generation; var closed = -generation;
        var flags = grid.Flags; var heights = legacy ? grid.LegacyZ : grid.WalkZ;
        var address = legacy ? grid.TileCount : grid.SlotCapacity;
        var nodes = legacy ? address : grid.ActiveNodeCount;
        if (ws.AddressRange < address || ws.NodeCapacity < nodes + 1) throw new ArgumentException("Workspace does not fit the graph.");
        var goal = req.Goals ?? GoalResolver.Resolve(grid, actor, req.GoalX, req.GoalY, req.Occupancy);
        var start = req.Start;
        var startSlot = StartSlot(start, legacy);
        if (Precheck(actor, goal, startSlot, legacy) is { } early) return early;
        var first = startSlot < 0 ? address : startSlot;
        stamps[first] = generation; costs[first] = 0; parents[first] = -1;
        sequences[first] = ws.NextSequence++;
        ws.Insert(first, PathTieBreak.Key(PathTieBreak.Heuristic(start.X, start.Y, goal.X, goal.Y),
            start.X, start.Y, goal.X, goal.Y, 0, sequences[first]));
        var cap = req.Complete ? nodes + 1 : settings.MaxExpansionsPerSearch ?? nodes + 1;
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
            var from = current == address ? start : grid.Position(current, legacy);
            if (current == address && !grid.InBounds(start.X, start.Y)) continue;
            var g = costs[current] + 1;
            foreach (var (dx, dy) in PathTieBreak.Neighbours)
            {
                if (!diagonal && dx != 0 && dy != 0) continue;
                var x = from.X + dx; var y = from.Y + dy;
                if ((uint)x >= width || (uint)y >= height) continue;
                var tile = y * width + x;
                var surfaces = legacy ? 1 : grid.SurfaceCount(tile);
                for (var ordinal = 0; ordinal < surfaces; ordinal++)
                {
                    var next = legacy ? tile : grid.SurfaceAt(tile, ordinal);
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
        }
        return PathOutcome.Unreachable;
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
