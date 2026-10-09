using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class BedExitTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BedOccupantCanExitDiagonallyBesideItsOwnFootprint(bool layered, bool across)
    {
        var settings = new PathfindingSettings { LayeringEnabled = layered };
        var (grid, inputs, compiler) = NavTest.Create(2, 2, settings,
            across ? [0, 0, 17, 0] : [0, 17, 0, 0]);
        inputs.Publish(NavTest.Record(1, 1, across ? [0, 1] : [0, 2], h: 1.9, interaction: InteractionType.Bed)
            with
        { Rotation = across ? 2 : 0 });
        compiler.ApplyNow();
        var rules = new MovementRules(grid, settings);
        var target = grid.Position(3);

        foreach (var start in new[] { grid.Position(0), new NavPosition(0, 0, 0) }) {
            foreach (var view in Enum.GetValues<OccupancyView>()) {
                Assert.Equal(StepReason.Ok, rules.CanStep(new(), start, target, StepPurpose.Goal, view).Reason);
            }

            var route = new Route();
            Assert.Equal(PathOutcome.Found, new PathSearch(grid, settings).Find(new(new(), start, 1, 1),
                new PathWorkspace(grid.SlotCapacity, grid.ActiveNodeCount), route));
            Assert.Equal([grid.Reference(3)], route.Steps.ToArray());
        }

        // The exit exception does not turn the bed into a corridor or change how clicks select its pillow.
        var bedFoot = grid.Position(across ? 1 : 2);
        Assert.Equal(StepReason.NotStandable,
            rules.CanStep(new(), grid.Position(0), bedFoot, StepPurpose.Transit, OccupancyView.Planning).Reason);
        Assert.Equal(StepReason.CornerBlocked,
            rules.CanStep(new(), target, grid.Position(0), StepPurpose.Goal, OccupancyView.Planning).Reason);
        var goal = GoalResolver.ResolveClick(grid, new(), target, bedFoot.X, bedFoot.Y, null);
        Assert.Equal((0, 0), (goal.X, goal.Y));
    }

    [Fact]
    public void BedExitPreservesLocksVoidAndDestinationChecks()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 2, z: [0, 17, 0, 0]);
        inputs.Publish(NavTest.Record(1, 1, [0, 2], h: 1.9, interaction: InteractionType.Bed));
        compiler.ApplyNow();
        var rules = new MovementRules(grid, new());
        var start = grid.Position(0);
        var target = grid.Position(3);
        var occupancy = new PlanningOccupancy(grid.SlotCapacity);
        occupancy.Targets[3] = TargetOccupancy.Stationary;
        Assert.Equal(StepReason.Occupied, rules.CanStep(new(), start, target, StepPurpose.Goal, OccupancyView.Execution, occupancy).Reason);
        Assert.Equal(StepReason.CornerBlocked,
            new MovementRules(grid, new() { CornerRule = CornerRule.Strict }).CanStep(new(), start, target, StepPurpose.Goal, OccupancyView.Planning).Reason);

        grid.FloorLocks[2] = 1;
        inputs.MarkDirty(2);
        compiler.ApplyNow();
        Assert.Equal(StepReason.CornerBlocked, rules.CanStep(new(), start, target, StepPurpose.Goal, OccupancyView.Planning).Reason);
        grid.FloorLocks[2] = 0;
        inputs.MarkDirty(2);
        compiler.ApplyNow();
        grid.TileVoid[1] = true;
        Assert.Equal(StepReason.CornerVoid, rules.CanStep(new(), start, target, StepPurpose.Goal, OccupancyView.Planning).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnotherBedOrAnActorBelowTheBedDoesNotProvideExitClearance(bool differentBed)
    {
        var settings = new PathfindingSettings { LayeringEnabled = true };
        var (grid, inputs, compiler) = NavTest.Create(2, 2, settings, [0, 17, 0, 0]);
        inputs.Publish(NavTest.Record(1, 1, [0], z: 2, h: 1.9, interaction: InteractionType.Bed));
        inputs.Publish(NavTest.Record(differentBed ? 2u : 1u, 2, differentBed ? [2] : [0, 2], z: 2, h: 1.9, interaction: InteractionType.Bed));
        compiler.ApplyNow();
        var bed = grid.SlotOf(new SurfaceRef(0, 1, SurfaceKind.BedBase));
        var start = differentBed ? grid.Position(bed) : new NavPosition(0, 0, 0);
        // Remove the open floor flank so only the elevated bed could grant clearance.
        var floor = grid.SlotOf(new SurfaceRef(2, 0, SurfaceKind.Floor));
        grid.Flags[floor] = NavFlags.None;
        Assert.Equal(StepReason.CornerBlocked, new MovementRules(grid, settings)
            .CanStep(new(), start, grid.Position(3), StepPurpose.Goal, OccupancyView.Planning).Reason);
    }
}
