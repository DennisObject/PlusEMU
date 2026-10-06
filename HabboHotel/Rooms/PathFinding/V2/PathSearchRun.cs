using System.Runtime.CompilerServices;

namespace Plus.HabboHotel.Rooms.PathFinding;

// One warmed search over a workspace. A struct so the hot path stays allocation-free.
internal readonly struct PathSearchRun
{
    private readonly NavGrid _grid;
    private readonly MovementRules _rules;
    private readonly PathfindingSettings _settings;
    private readonly SearchRequest _req;
    private readonly PathWorkspace _ws;
    private readonly bool _legacy, _diagonal;
    private readonly int _address, _nodes, _generation, _closed;
    private readonly int[] _stamps, _costs, _parents, _sequences;
    private readonly NavFlags[] _flags;
    private readonly double[] _heights;

    private readonly AcceptedGoal _goal;

    public PathSearchRun(NavGrid grid, MovementRules rules, PathfindingSettings settings, in SearchRequest req, PathWorkspace ws,
        in AcceptedGoal goal)
    {
        _grid = grid;
        _rules = rules;
        _settings = settings;
        _req = req;
        _ws = ws;
        _goal = goal;
        _legacy = req.Actor.LegacyOverride;
        _diagonal = req.Actor.DiagonalEnabled;
        _stamps = ws.Stamp;
        _costs = ws.G;
        _parents = ws.Parent;
        _sequences = ws.Sequence;
        _generation = ws.Generation;
        _closed = -_generation;
        _flags = grid.Flags;
        _heights = _legacy ? grid.LegacyZ : grid.WalkZ;
        _address = _legacy ? grid.TileCount : grid.SlotCapacity;
        _nodes = _legacy ? _address : grid.ActiveNodeCount;
    }

    public PathOutcome Execute(int startSlot, Route into)
    {
        var first = Seed(startSlot);
        var cap = _req.Complete ? _nodes + 1 : _settings.MaxExpansionsPerSearch ?? _nodes + 1;

        while (_ws.Count > 0) {
            var current = _ws.Pop();

            if (_goal.Contains(current)) {
                Reconstruct(current, first, into);

                return PathOutcome.Found;
            }

            if (_ws.Expansions >= cap) {
                return PathOutcome.BudgetCancelled;
            }

            _ws.Expansions++;
            _stamps[current] = -_generation;
            var from = current == _address ? _req.Start : _grid.Position(current, _legacy);

            if (current == _address && !_grid.InBounds(_req.Start.X, _req.Start.Y)) {
                continue;
            }

            ExpandNeighbours(current, from);
        }

        return PathOutcome.Unreachable;
    }

    private int Seed(int startSlot)
    {
        var start = _req.Start;
        var first = startSlot < 0 ? _address : startSlot;
        _stamps[first] = _generation;
        _costs[first] = 0;
        _parents[first] = -1;
        _sequences[first] = _ws.NextSequence++;
        _ws.Insert(first, PathTieBreak.Key(PathTieBreak.Heuristic(start.X, start.Y, _goal.X, _goal.Y),
            start.X, start.Y, _goal.X, _goal.Y, 0, _sequences[first]));

        return first;
    }

    private void ExpandNeighbours(int current, in NavPosition from)
    {
        var g = _costs[current] + 1;

        foreach (var (dx, dy) in PathTieBreak.Neighbours) {
            if (!_diagonal && dx != 0 && dy != 0) {
                continue;
            }

            var x = from.X + dx;
            var y = from.Y + dy;

            if ((uint)x >= _grid.Width || (uint)y >= _grid.Height) {
                continue;
            }

            var tile = y * _grid.Width + x;
            var surfaces = _legacy ? 1 : _grid.SurfaceCount(tile);

            for (var ordinal = 0; ordinal < surfaces; ordinal++) {
                Relax(current, from, g, x, y, _legacy ? tile : _grid.SurfaceAt(tile, ordinal));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Relax(int current, in NavPosition from, int g, int x, int y, int next)
    {
        var stamp = _stamps[next];

        if (stamp == _closed) {
            return;
        }

        var seen = stamp == _generation;

        if (seen && g >= _costs[next] || !_legacy && (_flags[next] & ~NavFlags.FloorLocked) == 0) {
            return;
        }

        var purpose = _goal.Contains(next) ? StepPurpose.Goal : StepPurpose.Transit;
        var to = new NavPosition(x, y, _heights[next], next);
        _ws.CanStepCalls++;

        if (!_rules.CanStepKnownNeighbour(_req.Actor, from, to, next, purpose, OccupancyView.Planning, _req.Occupancy).Ok) {
            return;
        }

        _costs[next] = g;
        _parents[next] = current;

        if (!seen) {
            _stamps[next] = _generation;
            _sequences[next] = _ws.NextSequence++;
        }

        var key = PathTieBreak.Key(g + PathTieBreak.Heuristic(x, y, _goal.X, _goal.Y), x, y,
            _goal.X, _goal.Y, _legacy ? (byte)0 : _grid.Ordinal[next], _sequences[next]);

        if (seen) {
            _ws.Decrease(next, key);
        }
        else {
            _ws.Insert(next, key);
        }
    }

    private void Reconstruct(int goal, int start, Route into)
    {
        var count = 0;

        for (var n = goal; n != start; n = _parents[n]) {
            count++;
        }

        into.EnsureCapacity(count);
        into.Count = count;
        into.GridVersion = _grid.Version;

        for (var n = goal; n != start; n = _parents[n]) {
            into.Set(--count, _legacy ? new(n, 0, SurfaceKind.Floor) : _grid.Reference(n));
        }

        into.GoalSurface = _legacy ? new(goal, 0, SurfaceKind.Floor) : _grid.Reference(goal);
    }
}
