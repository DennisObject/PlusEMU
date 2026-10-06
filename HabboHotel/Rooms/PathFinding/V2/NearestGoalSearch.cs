namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class NearestGoalSearch(NavGrid grid, PathfindingSettings settings)
{
    private readonly MovementRules _rules = new(grid, settings);
    private PathWorkspace _workspace = null!;
    private SearchRequest _request;
    private int _head, _tail, _best, _first;
    private (long Chebyshev, ulong Euclid) _distance;

    public PathOutcome Find(in SearchRequest request, PathWorkspace workspace, Route route)
    {
        _workspace = workspace;
        _request = request;
        route.Clear();
        route.View = request.Actor.LegacyOverride ? GraphView.LegacyTile : GraphView.Surface;
        workspace.Begin();
        _head = _tail = 0;
        _best = -1;
        _distance = (long.MaxValue, ulong.MaxValue);
        _first = StartSlot(request.Start);
        workspace.Stamp[_first] = workspace.Generation;
        workspace.Parent[_first] = -1;
        workspace.HeapNode[_tail++] = _first;

        if (_first != grid.SlotCapacity)
        {
            Consider(_first);
        }

        var cap = settings.MaxExpansionsPerSearch ?? (_request.Actor.LegacyOverride ? grid.SlotCapacity : grid.ActiveNodeCount) + 1;

        while (_head < _tail)
        {
            if (workspace.Expansions++ >= cap)
            {
                return PathOutcome.BudgetCancelled;
            }

            Expand(workspace.HeapNode[_head++]);
        }

        if (_best < 0)
        {
            return PathOutcome.Unreachable;
        }

        if (_best == _first)
        {
            return PathOutcome.AlreadyThere;
        }

        BuildRoute(route);

        return PathOutcome.Found;
    }

    private int StartSlot(in NavPosition start)
    {
        if (!grid.InBounds(start.X, start.Y))
        {
            return grid.SlotCapacity;
        }

        var tile = grid.Tile(start.X, start.Y);

        if (_request.Actor.LegacyOverride)
        {
            return tile;
        }

        for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++)
        {
            if (grid.WalkZ[grid.SurfaceAt(tile, ordinal)] == start.Z)
            {
                return grid.SurfaceAt(tile, ordinal);
            }
        }

        return grid.SlotCapacity;
    }

    private void Expand(int current)
    {
        var from = current == grid.SlotCapacity ? _request.Start : grid.Position(current, _request.Actor.LegacyOverride);

        foreach (var (dx, dy) in PathTieBreak.Neighbours)
        {
            if (!_request.Actor.DiagonalEnabled && dx != 0 && dy != 0)
            {
                continue;
            }

            var x = from.X + dx;
            var y = from.Y + dy;

            if (!grid.InBounds(x, y))
            {
                continue;
            }

            var tile = grid.Tile(x, y);

            if (_request.Actor.LegacyOverride)
            {
                Visit(current, from, tile);
                continue;
            }

            for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++)
            {
                Visit(current, from, grid.SurfaceAt(tile, ordinal));
            }
        }
    }

    private void Visit(int current, in NavPosition from, int slot)
    {
        if (_workspace.Stamp[slot] == _workspace.Generation)
        {
            return;
        }

        var legacy = _request.Actor.LegacyOverride;
        var purpose = legacy || (grid.Flags[slot] & NavFlags.Transit) != 0 ? StepPurpose.Transit : StepPurpose.Goal;
        _workspace.CanStepCalls++;

        if (!_rules.CanStep(_request.Actor, from, grid.Position(slot, legacy), purpose,
            OccupancyView.Planning, _request.Occupancy).Ok)
        {
            return;
        }

        _workspace.Stamp[slot] = _workspace.Generation;
        _workspace.Parent[slot] = current;
        Consider(slot);

        if (purpose == StepPurpose.Transit)
        {
            _workspace.HeapNode[_tail++] = slot;
        }
    }

    private void Consider(int slot)
    {
        var point = grid.Position(slot);

        if (!GoalResolver.Resolve(grid, _request.Actor, point.X, point.Y, _request.Occupancy).Contains(slot))
        {
            return;
        }

        var dx = (long)point.X - _request.GoalX;
        var dy = (long)point.Y - _request.GoalY;
        var chebyshev = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var distance = (chebyshev, (ulong)(dx * dx) + (ulong)(dy * dy));

        if (distance.CompareTo(_distance) >= 0)
        {
            return;
        }

        _distance = distance;
        _best = slot;
    }

    private SurfaceRef Reference(int slot) => _request.Actor.LegacyOverride
        ? new(slot, 0, SurfaceKind.Floor) : grid.Reference(slot);

    private void BuildRoute(Route route)
    {
        var count = 0;

        for (var node = _best; node != _first; node = _workspace.Parent[node])
        {
            count++;
        }

        route.EnsureCapacity(count);
        route.Count = count;
        route.GridVersion = grid.Version;

        for (var node = _best; node != _first; node = _workspace.Parent[node])
        {
            route.Set(--count, Reference(node));
        }

        route.GoalSurface = Reference(_best);
    }
}
