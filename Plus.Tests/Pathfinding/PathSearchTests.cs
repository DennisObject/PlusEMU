using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class PathSearchTests
{
    [Fact]
    public void FullProductionSearchMatchesBfsOnTenThousandSeededGrids()
    {
        var random = new Random(20261003);
        for (var iteration = 0; iteration < 10000; iteration++)
        {
            var w = random.Next(2, 11); var h = random.Next(2, 11); var size = w * h;
            var settings = new PathfindingSettings { Profile = iteration % 2 == 0 ? "plus" : "habbo2013", CornerRule = (CornerRule)(iteration % 3) };
            var heights = Enumerable.Range(0, size).Select(_ => new[] { -5.0, 0.0, 1.25, 1.5, 1.5004, 3.0 }[random.Next(6)]).ToArray();
            var states = Enumerable.Range(0, size).Select(_ => random.Next(6) switch { 0 => SquareState.Blocked, 1 => SquareState.Seat, _ => SquareState.Open }).ToArray();
            var (grid, _, _) = NavTest.Create(w, h, settings, heights, states);
            var occupancy = new PlanningOccupancy(size);
            for (var t = 0; t < size; t++)
            {
                if (random.Next(12) == 0) { grid.Flags[t] |= NavFlags.GuildGate; grid.GroupId[t] = 7; }
                if (random.Next(12) == 0) grid.Flags[t] |= NavFlags.FloorLocked;
                occupancy.Targets[t] = random.Next(10) switch { 0 => TargetOccupancy.Stationary, 1 => TargetOccupancy.Walking, 2 => TargetOccupancy.OffGraph, _ => TargetOccupancy.None };
            }
            var startTile = random.Next(size); var goalTile = random.Next(size);
            var start = grid.Position(startTile);
            if (iteration % 7 == 0) start = start with { Z = start.Z + 0.0004, Slot = -1 };
            occupancy.Targets[startTile] = TargetOccupancy.None;
            foreach (var profile in Enumerable.Range(0, 6))
            {
                var actor = new ActorProfile { LegacyOverride = profile == 1, IgnoreUsers = profile == 2,
                    IgnoreStepHeight = profile == 3, Walkthrough = profile == 4, DiagonalEnabled = profile != 5 };
                if (iteration % 2 == 0) actor.SetMembership(7, true);
                var request = new SearchRequest(actor, start, goalTile % w, goalTile / w, occupancy);
                var workspace = new PathWorkspace(size, actor.LegacyOverride ? size : grid.ActiveNodeCount);
                var route = new Route();
                var outcome = new PathSearch(grid, settings).Find(request, workspace, route);
                var (expected, length) = Bfs(grid, settings, request);
                Assert.True(expected == outcome, $"seed case={iteration} profile={profile}: BFS {expected}, A* {outcome}");
                Assert.Equal(length, route.Count);
                ValidateRoute(grid, settings, request, route);
            }
        }
    }

    internal static (PathOutcome Outcome, int Length) Bfs(NavGrid grid, PathfindingSettings settings, SearchRequest request)
    {
        if (!grid.InBounds(request.GoalX, request.GoalY)) return (PathOutcome.InvalidGoal, 0);
        var goal = request.Goals ?? GoalResolver.Resolve(grid, request.Actor, request.GoalX, request.GoalY, request.Occupancy);
        var startTile = grid.Tile(request.Start.X, request.Start.Y);
        var virtualStart = !request.Actor.LegacyOverride && (!grid.Active(startTile) || grid.WalkZ[startTile] != request.Start.Z);
        if (!virtualStart && goal.Contains(startTile)) return (PathOutcome.AlreadyThere, 0);
        if (goal.Slot < 0) return (PathOutcome.InvalidGoal, 0);
        var rules = new MovementRules(grid, settings);
        var queue = new Queue<(NavPosition Position, int Distance)>();
        var visited = new HashSet<int>();
        queue.Enqueue((request.Start, 0)); if (!virtualStart) visited.Add(startTile);
        while (queue.TryDequeue(out var current))
        {
            foreach (var (dx, dy) in PathTieBreak.Neighbours)
            {
                if (!request.Actor.DiagonalEnabled && dx != 0 && dy != 0) continue;
                var x = current.Position.X + dx; var y = current.Position.Y + dy;
                if (!grid.InBounds(x, y)) continue;
                var tile = grid.Tile(x, y);
                if (visited.Contains(tile) || !request.Actor.LegacyOverride && !grid.Active(tile)) continue;
                var next = grid.Position(tile, request.Actor.LegacyOverride);
                if (!rules.CanStep(request.Actor, current.Position, next, goal.Contains(tile) ? StepPurpose.Goal : StepPurpose.Transit,
                    OccupancyView.Planning, request.Occupancy).Ok) continue;
                if (goal.Contains(tile)) return (PathOutcome.Found, current.Distance + 1);
                visited.Add(tile); queue.Enqueue((next, current.Distance + 1));
            }
        }
        return (PathOutcome.Unreachable, 0);
    }

    private static void ValidateRoute(NavGrid grid, PathfindingSettings settings, SearchRequest request, Route route)
    {
        var rules = new MovementRules(grid, settings); var from = request.Start;
        for (var i = 0; i < route.Count; i++)
        {
            var next = grid.Position(route.Steps[i].Tile, request.Actor.LegacyOverride);
            Assert.True(rules.CanStep(request.Actor, from, next, i == route.Count - 1 ? StepPurpose.Goal : StepPurpose.Transit, OccupancyView.Planning, request.Occupancy).Ok);
            from = next;
        }
    }

    [Fact]
    public void GoldenOpenRoutesAreDiagonalFirstInEveryOctantAndRepeatExactly()
    {
        var (grid, _, _) = NavTest.Create(15, 15);
        var search = new PathSearch(grid, new()); var actor = new ActorProfile();
        using var lease = PathWorkspacePool.Rent(grid.SlotCapacity, grid.ActiveNodeCount);
        foreach (var (x, y) in new[] { (5, 2), (2, 5), (5, 0), (5, 5) })
        foreach (var sx in new[] { -1, 1 }) foreach (var sy in new[] { -1, 1 })
        {
            var request = new SearchRequest(actor, grid.Position(grid.Tile(7, 7)), 7 + sx * x, 7 + sy * y);
            var expected = Enumerable.Range(1, Math.Max(x, y)).Select(i => grid.Tile(7 + sx * Math.Min(i, x), 7 + sy * Math.Min(i, y))).ToArray();
            for (var repeat = 0; repeat < 100; repeat++)
            {
                var route = new Route(); Assert.Equal(PathOutcome.Found, search.Find(request, lease.Workspace, route));
                Assert.Equal(expected, route.Steps.ToArray().Select(s => s.Tile));
            }
        }
    }

    [Fact]
    public void GoldensForUShapeSymmetricObstacleAndStairs()
    {
        Check(5, 5, ["00000", "0xxx0", "0x0x0", "0x0x0", "00000"], 2, 2, 0, 2,
            [17, 22, 21, 20, 15, 10]);
        Check(5, 3, ["00000", "00x00", "00000"], 0, 1, 4, 1, [1, 2, 3, 9]);
        var settings = new PathfindingSettings();
        var (stairs, _, _) = NavTest.Create(5, 1, settings, [0, 1.5, 3, 4.5, 6]);
        var route = new Route(); Assert.Equal(PathOutcome.Found, new PathSearch(stairs, settings).Find(new(new(), stairs.Position(0), 4, 0), new(5, 5), route));
        Assert.Equal(new[] { 1, 2, 3, 4 }, route.Steps.ToArray().Select(s => s.Tile));

        static void Check(int w, int h, string[] rows, int sx, int sy, int gx, int gy, int[] expected)
        {
            var (grid, _, _) = NavTest.Create(w, h, states: string.Concat(rows).Select(c => c == 'x' ? SquareState.Blocked : SquareState.Open).ToArray());
            var request = new SearchRequest(new(), grid.Position(grid.Tile(sx, sy)), gx, gy);
            var route = new Route(); Assert.Equal(PathOutcome.Found, new PathSearch(grid, new()).Find(request, new(w * h, grid.ActiveNodeCount), route));
            Assert.Equal(expected, route.Steps.ToArray().Select(s => s.Tile));
        }
    }

    [Fact]
    public void OutcomesPrecheckOrderAcceptedSetAndVirtualStarts()
    {
        var (grid, _, _) = NavTest.Create(3, 1);
        var search = new PathSearch(grid, new()); var ws = new PathWorkspace(3, 3); var route = new Route();
        Assert.Equal(PathOutcome.InvalidGoal, search.Find(new(new(), grid.Position(0), 9, 0), ws, route));
        Assert.Equal(PathOutcome.AlreadyThere, search.Find(new(new(), grid.Position(0), 0, 0), ws, route));
        Assert.Equal(0, ws.Expansions);
        Assert.Equal(PathOutcome.InvalidGoal, search.Find(new(new(), grid.Position(0), 0, 0, Goals: new(0, 0, -1)), ws, route));
        Assert.Equal(PathOutcome.Found, search.Find(new(new(), new(0, 0, 1), 1, 0), ws, route));
        Assert.Equal(1, route.Count);
        Assert.Equal(PathOutcome.BudgetCancelled, new PathSearch(grid, new() { MaxExpansionsPerSearch = 1 }).Find(new(new(), grid.Position(0), 2, 0), ws, route));
        var (isolated, _, _) = NavTest.Create(3, 1, states: [SquareState.Open, SquareState.Blocked, SquareState.Open]);
        Assert.Equal(PathOutcome.Unreachable, new PathSearch(isolated, new()).Find(new(new(), isolated.Position(0), 2, 0), ws, route));
        Assert.Equal(0, ws.Expansions);
        Assert.Equal(PathOutcome.Found, new PathSearch(isolated, new()).Find(new(new() { LegacyOverride = true }, isolated.Position(0), 2, 0), ws, route));
        var (height, _, _) = NavTest.Create(2, 1, z: [0, 1.5004]);
        Assert.Equal(PathOutcome.Unreachable, new PathSearch(height, new()).Find(new(new(), height.Position(0), 1, 0), ws, route));
        Assert.Equal(0, ws.Expansions); // HasWayIn.
    }

    [Fact]
    public void SparseHighSlotsAndWorkspaceLeasesKeepIndexSpacesSeparate()
    {
        var states = Enumerable.Repeat(SquareState.Blocked, 4096).ToArray(); states[4095] = SquareState.Open;
        var (grid, _, _) = NavTest.Create(64, 64, states: states);
        Assert.Equal(4096, grid.SlotCapacity); Assert.Equal(1, grid.ActiveNodeCount);
        using var first = PathWorkspacePool.Rent(grid.SlotCapacity, grid.ActiveNodeCount);
        using var second = PathWorkspacePool.Rent(grid.SlotCapacity, grid.ActiveNodeCount);
        Assert.NotSame(first.Workspace, second.Workspace);
        var route = new Route();
        Assert.Equal(PathOutcome.Found, new PathSearch(grid, new() { CornerRule = CornerRule.None }).Find(new(new(), new(62, 63, 0), 63, 63), first.Workspace, route));
        Assert.Equal(4095, route.Steps[0].Tile);
        first.Workspace.Generation = int.MaxValue - 1;
        Assert.Equal(PathOutcome.Found, new PathSearch(grid, new()).Find(new(new(), new(62, 63, 0), 63, 63), first.Workspace, route));
        Assert.Equal(1, first.Workspace.Generation);
    }

    [Fact]
    public void IndexedHeapRepairsDecreaseAndKeepsFirstInsertionSequence()
    {
        var ws = new PathWorkspace(4, 4); ws.Begin();
        ws.Insert(0, 100); ws.Insert(1, 200); ws.Insert(2, 300); ws.Insert(3, 400);
        ws.Sequence[3] = 3; ws.Decrease(3, 50);
        Assert.Equal(3, ws.Pop()); Assert.Equal(3, ws.Sequence[3]); Assert.Equal(0, ws.Pop());
        Assert.Equal(1, ws.Pop()); Assert.Equal(2, ws.Pop());
    }
}
