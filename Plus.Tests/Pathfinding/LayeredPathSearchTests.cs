using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class LayeredPathSearchTests
{
    // W W F W W
    // F S B S F    S = 1.0 step, B = zero-height bridge at 2.0 over open floor
    // W W F W W
    private static (NavGrid Grid, NavInputs Inputs, NavGridCompiler Compiler) Crossing(bool layered = true)
    {
        var (grid, inputs, compiler) = NavTest.Create(5, 3, new PathfindingSettings { LayeringEnabled = layered });
        uint id = 10;

        foreach (var t in new[] { 0, 1, 3, 4, 10, 11, 13, 14 }) {
            inputs.Publish(NavTest.Record(id, id++, [t], h: 1, walkable: false));
        }

        inputs.Publish(NavTest.Record(30, 30, [6], h: 1));
        inputs.Publish(NavTest.Record(31, 31, [8], h: 1));
        inputs.Publish(NavTest.Record(32, 32, [7], z: 2));
        compiler.ApplyNow();

        return (grid, inputs, compiler);
    }

    [Fact]
    public void FloorRouteWalksUnderTheBridgeOnlyWithLayering()
    {
        foreach (var layered in new[] { true, false }) {
            var (grid, _, _) = Crossing(layered);
            var route = new Route();
            var outcome = Find(grid, new(new(), grid.Position(2), 2, 2), route);

            if (!layered) {
                Assert.Equal(PathOutcome.Unreachable, outcome);
                continue;
            }

            Assert.Equal(PathOutcome.Found, outcome);
            Assert.Equal([new SurfaceRef(7, 0, SurfaceKind.Floor), new SurfaceRef(12, 0, SurfaceKind.Floor)], route.Steps.ToArray());
        }
    }

    [Fact]
    public void BridgeOccupantDoesNotBlockTheFloorBelowAndAFloorOccupantForcesTheDeck()
    {
        var (grid, _, _) = Crossing();
        var bridge = grid.SlotOf(new SurfaceRef(7, 32, SurfaceKind.Top));
        Assert.True(bridge >= grid.TileCount);
        var occupancy = new PlanningOccupancy(grid.SlotCapacity);
        occupancy.Targets[bridge] = TargetOccupancy.Stationary;
        var route = new Route();
        Assert.Equal(PathOutcome.Found, Find(grid, new(new(), grid.Position(2), 2, 2, occupancy), route));
        Assert.Equal(7, grid.SlotOf(route.Steps[0]));
        occupancy.Targets[bridge] = TargetOccupancy.None;
        occupancy.Targets[7] = TargetOccupancy.Stationary;
        Assert.Equal(PathOutcome.Found, Find(grid, new(new(), grid.Position(5), 4, 1, occupancy), route));
        Assert.Equal([6, bridge, 8, 9], route.Steps.ToArray().Select(grid.SlotOf));
        Assert.Equal([1d, 2d, 1d, 0d], route.Steps.ToArray().Select(s => grid.WalkZ[grid.SlotOf(s)]));
    }

    [Fact]
    public void VerticalGapIsNeverAnEdge()
    {
        var (grid, _, _) = Crossing();
        var bridge = grid.SlotOf(new SurfaceRef(7, 32, SurfaceKind.Top));
        var route = new Route();
        var floorOnly = new AcceptedGoal(2, 1, 7);
        Assert.Equal(PathOutcome.Found, Find(grid, new(new(), grid.Position(bridge), 2, 1, Goals: floorOnly), route));
        Assert.Equal(2, route.Count);
        Assert.NotEqual(7, route.Steps[0].Tile);
        Assert.Equal(new SurfaceRef(7, 0, SurfaceKind.Floor), route.GoalSurface);
    }

    [Fact]
    public void AnyEligibleSurfaceOfTheClickedTileIsAGoalAndTheFirstPoppedWins()
    {
        var (grid, _, _) = Crossing();
        var bridge = grid.SlotOf(new SurfaceRef(7, 32, SurfaceKind.Top));
        var actor = new ActorProfile();
        var goal = GoalResolver.Resolve(grid, actor, 2, 1, null);
        Assert.True(goal.Contains(7));
        Assert.True(goal.Contains(bridge));
        Assert.False(goal.Contains(6));
        var route = new Route();
        Assert.Equal(PathOutcome.Found, Find(grid, new(actor, grid.Position(6), 2, 1), route));
        Assert.Equal(new SurfaceRef(7, 0, SurfaceKind.Floor), route.GoalSurface);
        Assert.Equal(PathOutcome.AlreadyThere, Find(grid, new(actor, grid.Position(bridge), 2, 1), route));
        var occupancy = new PlanningOccupancy(grid.SlotCapacity);
        occupancy.Targets[7] = TargetOccupancy.Stationary;
        goal = GoalResolver.Resolve(grid, actor, 2, 1, occupancy);
        Assert.False(goal.Contains(7));
        Assert.True(goal.Contains(bridge));
        Assert.Equal(PathOutcome.Found, Find(grid, new(actor, grid.Position(6), 2, 1, occupancy), route));
        Assert.Equal(new SurfaceRef(7, 32, SurfaceKind.Top), route.GoalSurface);
        occupancy.Targets[bridge] = TargetOccupancy.Stationary;
        Assert.Equal(-1, GoalResolver.Resolve(grid, actor, 2, 1, occupancy).Slot);
    }

    [Fact]
    public void AStartSurfaceIsMatchedByItsExactHeight()
    {
        var (grid, _, _) = Crossing();
        var route = new Route();
        Assert.Equal(PathOutcome.AlreadyThere, Find(grid, new(new(), new NavPosition(2, 1, 2), 2, 1), route));
        Assert.Equal(PathOutcome.Found, Find(grid, new(new(), new NavPosition(2, 1, 2), 3, 1), route));
        Assert.Equal([8], route.Steps.ToArray().Select(grid.SlotOf));
        Assert.Equal(PathOutcome.Found, Find(grid, new(new(), new NavPosition(2, 1, 1.25), 2, 0), route));
        Assert.Equal([2], route.Steps.ToArray().Select(grid.SlotOf));
    }

    [Fact]
    public void ADiagonalFlankIsOpenWhenAnyOfItsSurfacesIsOpen()
    {
        foreach (var layered in new[] { true, false }) {
            // The tile's own slot keeps the closed deck; the open floor reappears in an overflow slot.
            var (grid, inputs, compiler) = NavTest.Create(2, 2, new PathfindingSettings { LayeringEnabled = layered });
            inputs.Publish(NavTest.Record(10, 1, [1], z: 2));
            inputs.Publish(NavTest.Record(12, 2, [1], h: 1, walkable: false));
            inputs.Publish(NavTest.Record(11, 3, [2], h: 1, walkable: false));
            compiler.ApplyNow();
            inputs.Publish(NavTest.Record(12, 4, [1], h: 1, walkable: false, removed: true));
            compiler.ApplyNow();

            if (layered) {
                Assert.Equal((4, 1), (grid.SurfaceAt(1, 0), grid.SurfaceAt(1, 1)));
            }

            var result = new MovementRules(grid, new()).CanStep(new(), grid.Position(0), grid.Position(3), StepPurpose.Goal, OccupancyView.Execution);
            Assert.Equal(layered ? StepReason.Ok : StepReason.CornerBlocked, result.Reason);
            Assert.Equal(layered, new MovementRules(grid, new()).CanFlank(new(), grid.Position(0), 1, 0));
        }
    }

    [Fact]
    public void FullProductionSearchMatchesASurfaceBfsOnSeededLayeredGrids()
    {
        var random = new Random(20261004);

        for (var iteration = 0; iteration < 1500; iteration++) {
            var (grid, settings) = SeededLayeredGrid(random, iteration);

            if (SeededRequest(random, grid, iteration) is not { } request) {
                continue;
            }

            var route = new Route();
            var outcome = Find(grid, request, route, settings);
            var (expected, length) = SurfaceBfs(grid, settings, request);
            Assert.True(expected == outcome, $"case={iteration}: BFS {expected}, A* {outcome}");
            Assert.Equal(length, route.Count);
            Validate(grid, settings, request, route);
        }
    }

    private static (NavGrid Grid, PathfindingSettings Settings) SeededLayeredGrid(Random random, int iteration)
    {
        var w = random.Next(2, 9);
        var h = random.Next(2, 9);
        var settings = new PathfindingSettings
        {
            LayeringEnabled = true,
            MaxSurfacesPerTile = random.Next(1, 5),
            Profile = iteration % 2 == 0 ? "plus" : "habbo2013",
            CornerRule = (CornerRule)(iteration % 3)
        };
        var states = Enumerable.Range(0, w * h).Select(_ => random.Next(8) == 0 ? SquareState.Blocked : SquareState.Open).ToArray();
        var (grid, inputs, compiler) = NavTest.Create(w, h, settings, states: states);
        uint id = 1;

        for (var t = 0; t < w * h; t++) {
            for (var layer = random.Next(4); layer > 0; layer--) {
                inputs.Publish(SeededRecord(random, id++, t));
            }
        }

        compiler.ApplyNow();

        return (grid, settings);
    }

    private static NavItemRecord SeededRecord(Random random, uint id, int tile) => random.Next(4) switch
    {
        0 => NavTest.Record(id, id, [tile], z: random.Next(5) * 0.75, h: 0.5, walkable: false),
        1 => NavTest.Record(id, id, [tile], z: random.Next(5) * 0.75, h: 0, walkable: false, seat: true),
        _ => NavTest.Record(id, id, [tile], z: random.Next(6) * 0.75, h: random.Next(2) * 0.5)
    };

    private static SearchRequest? SeededRequest(Random random, NavGrid grid, int iteration)
    {
        var slots = Enumerable.Range(0, grid.SlotCapacity).Where(grid.Active).ToArray();

        if (slots.Length == 0) {
            return null;
        }

        var occupancy = new PlanningOccupancy(grid.SlotCapacity);

        foreach (var slot in slots) {
            occupancy.Targets[slot] = random.Next(10) switch { 0 => TargetOccupancy.Stationary, 1 => TargetOccupancy.Walking, _ => TargetOccupancy.None };
        }

        var start = slots[random.Next(slots.Length)];
        var goalTile = random.Next(grid.TileCount);
        occupancy.Targets[start] = TargetOccupancy.None;
        var actor = new ActorProfile { IgnoreStepHeight = iteration % 5 == 0, Walkthrough = iteration % 7 == 0, DiagonalEnabled = iteration % 11 != 0 };

        return new SearchRequest(actor, grid.Position(start), goalTile % grid.Width, goalTile / grid.Width, occupancy);
    }

    [Fact]
    public void WarmedLayeredSearchAllocatesNothing()
    {
        var (grid, _, _) = Crossing();
        var search = new PathSearch(grid, new());
        var occupancy = new PlanningOccupancy(grid.SlotCapacity);
        occupancy.Targets[7] = TargetOccupancy.Stationary;
        var request = new SearchRequest(new(), grid.Position(5), 4, 1, occupancy);
        using var lease = PathWorkspacePool.Rent(grid.SlotCapacity, grid.ActiveNodeCount);
        var route = new Route();

        for (var i = 0; i < 50; i++) {
            Assert.Equal(PathOutcome.Found, search.Find(request, lease.Workspace, route));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100; i++) {
            search.Find(request, lease.Workspace, route);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static PathOutcome Find(NavGrid grid, SearchRequest request, Route route, PathfindingSettings? settings = null)
    {
        using var lease = PathWorkspacePool.Rent(grid.SlotCapacity, request.Actor.LegacyOverride ? grid.TileCount : grid.ActiveNodeCount);

        return new PathSearch(grid, settings ?? new()).Find(request, lease.Workspace, route);
    }

    // Oracle: breadth-first over surface slots with the production step rule; same-tile surfaces are never adjacent.
    private static (PathOutcome Outcome, int Length) SurfaceBfs(NavGrid grid, PathfindingSettings settings, SearchRequest request)
    {
        var goal = GoalResolver.Resolve(grid, request.Actor, request.GoalX, request.GoalY, request.Occupancy);
        var startSlot = request.Start.Slot;

        if (goal.Contains(startSlot)) {
            return (PathOutcome.AlreadyThere, 0);
        }

        if (goal.Slot < 0) {
            return (PathOutcome.InvalidGoal, 0);
        }

        var rules = new MovementRules(grid, settings);
        var queue = new Queue<(NavPosition Position, int Distance)>();
        var visited = new HashSet<int> { startSlot };
        queue.Enqueue((request.Start, 0));

        while (queue.TryDequeue(out var current)) {
            foreach (var (dx, dy) in PathTieBreak.Neighbours) {
                if (!request.Actor.DiagonalEnabled && dx != 0 && dy != 0) {
                    continue;
                }

                var x = current.Position.X + dx;
                var y = current.Position.Y + dy;

                if (!grid.InBounds(x, y)) {
                    continue;
                }

                for (var ordinal = 0; ordinal < grid.SurfaceCount(grid.Tile(x, y)); ordinal++) {
                    var slot = grid.SurfaceAt(grid.Tile(x, y), ordinal);

                    if (visited.Contains(slot)) {
                        continue;
                    }

                    var next = grid.Position(slot);

                    if (!rules.CanStep(request.Actor, current.Position, next, goal.Contains(slot) ? StepPurpose.Goal : StepPurpose.Transit,
                        OccupancyView.Planning, request.Occupancy).Ok) {
                        continue;
                    }

                    if (goal.Contains(slot)) {
                        return (PathOutcome.Found, current.Distance + 1);
                    }

                    visited.Add(slot);
                    queue.Enqueue((next, current.Distance + 1));
                }
            }
        }

        return (PathOutcome.Unreachable, 0);
    }

    private static void Validate(NavGrid grid, PathfindingSettings settings, SearchRequest request, Route route)
    {
        var rules = new MovementRules(grid, settings);
        var from = request.Start;

        for (var i = 0; i < route.Count; i++) {
            var slot = grid.SlotOf(route.Steps[i]);
            Assert.True(slot >= 0);
            var next = grid.Position(slot);
            Assert.True(rules.CanStep(request.Actor, from, next, i == route.Count - 1 ? StepPurpose.Goal : StepPurpose.Transit,
                OccupancyView.Planning, request.Occupancy).Ok);
            from = next;
        }
    }
}
