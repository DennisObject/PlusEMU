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
        _workspace = workspace; _request = request;
        route.Clear(); route.View = request.Actor.LegacyOverride ? GraphView.LegacyTile : GraphView.Surface; workspace.Begin(); _head = _tail = 0; _best = -1; _distance = (long.MaxValue, ulong.MaxValue);
        _first = grid.InBounds(request.Start.X, request.Start.Y)
            && (request.Actor.LegacyOverride || grid.WalkZ[grid.Tile(request.Start.X, request.Start.Y)] == request.Start.Z)
            ? grid.Tile(request.Start.X, request.Start.Y) : grid.SlotCapacity;
        workspace.Stamp[_first] = workspace.Generation; workspace.Parent[_first] = -1;
        workspace.HeapNode[_tail++] = _first;
        if (_first != grid.SlotCapacity) Consider(_first);
        var cap = settings.MaxExpansionsPerSearch ?? (_request.Actor.LegacyOverride ? grid.SlotCapacity : grid.ActiveNodeCount) + 1;
        while (_head < _tail)
        {
            if (workspace.Expansions++ >= cap) return PathOutcome.BudgetCancelled;
            Expand(workspace.HeapNode[_head++]);
        }
        if (_best < 0) return PathOutcome.Unreachable;
        if (_best == _first) return PathOutcome.AlreadyThere;
        BuildRoute(route);
        return PathOutcome.Found;
    }

    private void Expand(int current)
    {
        var from = current == grid.SlotCapacity ? _request.Start : grid.Position(current, _request.Actor.LegacyOverride);
        foreach (var (dx, dy) in PathTieBreak.Neighbours)
        {
            if (!_request.Actor.DiagonalEnabled && dx != 0 && dy != 0) continue;
            var x = from.X + dx; var y = from.Y + dy;
            if (!grid.InBounds(x, y)) continue;
            var slot = grid.Tile(x, y);
            if (_workspace.Stamp[slot] == _workspace.Generation) continue;
            var purpose = _request.Actor.LegacyOverride || (grid.Flags[slot] & NavFlags.Transit) != 0 ? StepPurpose.Transit : StepPurpose.Goal;
            _workspace.CanStepCalls++;
            if (!_rules.CanStep(_request.Actor, from, grid.Position(slot, _request.Actor.LegacyOverride), purpose,
                OccupancyView.Planning, _request.Occupancy).Ok) continue;
            _workspace.Stamp[slot] = _workspace.Generation; _workspace.Parent[slot] = current;
            Consider(slot);
            if (purpose == StepPurpose.Transit) _workspace.HeapNode[_tail++] = slot;
        }
    }

    private void Consider(int slot)
    {
        var point = grid.Position(slot);
        if (GoalResolver.Resolve(grid, _request.Actor, point.X, point.Y, _request.Occupancy).Slot < 0) return;
        var dx = (long)point.X - _request.GoalX; var dy = (long)point.Y - _request.GoalY;
        var chebyshev = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var distance = (chebyshev, (ulong)(dx * dx) + (ulong)(dy * dy));
        if (distance.CompareTo(_distance) >= 0) return;
        _distance = distance; _best = slot;
    }

    private SurfaceRef Reference(int tile) => _request.Actor.LegacyOverride
        ? new(tile, 0, SurfaceKind.Floor) : grid.Reference(tile);

    private void BuildRoute(Route route)
    {
        var count = 0;
        for (var node = _best; node != _first; node = _workspace.Parent[node]) count++;
        route.EnsureCapacity(count); route.Count = count; route.GridVersion = grid.Version;
        for (var node = _best; node != _first; node = _workspace.Parent[node])
            route.Set(--count, Reference(node));
        route.GoalSurface = Reference(_best);
    }
}
