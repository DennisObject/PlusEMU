using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class MovementRulesTests
{
    [Theory]
    [InlineData("plus", 1.5)]
    [InlineData("habbo2013", 1.25)]
    public void HeightsAreDirectedInclusiveAndNeverRounded(string profile, double limit)
    {
        var settings = new PathfindingSettings { Profile = profile };

        foreach (var delta in new[] { -0.001, -0.0004, 0, 0.0004, 0.001 })
        {
            var (grid, _, _) = NavTest.Create(2, 1, settings, [0, limit + delta]);
            var rules = new MovementRules(grid, settings);
            Assert.Equal(delta <= 0, rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning).Ok);
            Assert.True(rules.CanStep(new()
            {
                IgnoreStepHeight = true
            }, grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning).Ok);
        }

        foreach (var drop in new[] { 3.9996, 4.0, 4.0004, 100.0 })
        {
            var (grid, _, _) = NavTest.Create(2, 1, settings, [drop, 0]);
            Assert.Equal(profile == "plus" || drop <= 4,
                new MovementRules(grid, settings).CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning).Ok);
        }
    }

    [Fact]
    public void SeatAccessUsesBaseNotTopAndGoalPurpose()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 1);
        inputs.Publish(NavTest.Record(1, 1, [1], z: 1.5, h: 8, seat: true));
        compiler.ApplyNow();
        var rules = new MovementRules(grid, new());
        Assert.Equal(1.5, grid.WalkZ[1]);
        Assert.True(rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Goal, OccupancyView.Planning).Ok);
        Assert.False(rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning).Ok);
    }

    [Theory]
    [InlineData(CornerRule.Official, 0, true)]
    [InlineData(CornerRule.Official, 1, true)]
    [InlineData(CornerRule.Official, 2, false)]
    [InlineData(CornerRule.Strict, 0, true)]
    [InlineData(CornerRule.Strict, 1, false)]
    [InlineData(CornerRule.None, 2, true)]
    public void CornerMatrix(CornerRule corner, int closed, bool expected)
    {
        var settings = new PathfindingSettings { CornerRule = corner };
        var (grid, inputs, compiler) = NavTest.Create(2, 2, settings);

        if (closed > 0)
        {
            inputs.Publish(NavTest.Record(1, 1, [1], walkable: false));
        }

        if (closed > 1)
        {
            inputs.Publish(NavTest.Record(2, 2, [2], walkable: false));
        }

        compiler.ApplyNow();
        Assert.Equal(expected, new MovementRules(grid, settings).CanStep(new(), grid.Position(0), grid.Position(3), StepPurpose.Transit, OccupancyView.Planning).Ok);
    }

    [Fact]
    public void OfficialNeverCrossesVoidAndUsersNeverCloseAFlank()
    {
        var (voidGrid, _, _) = NavTest.Create(2, 2, states: [SquareState.Open, SquareState.Blocked, SquareState.Open, SquareState.Open]);
        Assert.Equal(StepReason.CornerVoid, new MovementRules(voidGrid, new()).CanStep(new(), voidGrid.Position(0), voidGrid.Position(3), StepPurpose.Goal, OccupancyView.Planning).Reason);
        var (grid, _, _) = NavTest.Create(2, 2);
        var occupancy = new PlanningOccupancy(4);
        occupancy.Targets[1] = occupancy.Targets[2] = (TargetOccupancy)127;
        Assert.True(new MovementRules(grid, new()).CanStep(new(), grid.Position(0), grid.Position(3), StepPurpose.Transit, OccupancyView.Planning, occupancy).Ok);
    }

    [Fact]
    public void FlankKindsLocksMembershipAndHeightProfiles()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 2);
        var rules = new MovementRules(grid, new());
        var closed = new[] {
            NavTest.Record(1, 1, [1], seat: true),
            NavTest.Record(1, 2, [1], interaction: InteractionType.Bed),
            NavTest.Record(1, 3, [1], walkable: false, interaction: InteractionType.Gate),
            NavTest.Record(1, 4, [1], interaction: InteractionType.GuildGate) };

        foreach (var record in closed)
        {
            inputs.Publish(record);
            compiler.ApplyNow();
            Assert.False(rules.CanFlank(new(), grid.Position(0), 1, 0));
        }

        var member = new ActorProfile();
        member.SetMembership(7, true);
        Assert.True(rules.CanFlank(member, grid.Position(0), 1, 0));
        member.SetMembership(7, false);
        Assert.Equal(2, member.CapabilityVersion);
        grid.FloorLocks[1] = 1;
        inputs.MarkDirty(1);
        compiler.ApplyNow();
        member.SetMembership(7, true);
        Assert.False(rules.CanFlank(member, grid.Position(0), 1, 0));

        foreach (var profile in new[] { "plus", "habbo2013" })
        {
            var settings = new PathfindingSettings { Profile = profile };
            var (heights, _, _) = NavTest.Create(2, 2, settings, [0, 1.5004, -4.0004, 0]);
            var heightRules = new MovementRules(heights, settings);
            Assert.False(heightRules.CanFlank(new(), heights.Position(0), 1, 0));
            Assert.Equal(profile == "plus", heightRules.CanFlank(new(), heights.Position(0), 0, 1));
        }
    }

    [Fact]
    public void PrivilegedDispatchPrecedesStandabilityHeightCornersAndOccupancy()
    {
        var (grid, _, _) = NavTest.Create(2, 2, z: [0, 90, 90, 90], states: [SquareState.Open, SquareState.Blocked, SquareState.Blocked, SquareState.Blocked]);
        var occupancy = new PlanningOccupancy(4);
        occupancy.Targets[3] = (TargetOccupancy)127;
        var rules = new MovementRules(grid, new());
        Assert.True(rules.CanStep(new()
        {
            LegacyOverride = true
        }, grid.Position(0), grid.Position(3), StepPurpose.Goal, OccupancyView.Execution, occupancy).Ok);
        var actor = new ActorProfile { Interaction = new(0, 0, 1, 1) };
        Assert.True(rules.CanStep(actor, grid.Position(0), grid.Position(3), StepPurpose.Interaction, OccupancyView.Execution, occupancy).Ok);
        Assert.Equal(StepReason.InteractionDenied, rules.CanStep(actor, grid.Position(0), grid.Position(1), StepPurpose.Interaction, OccupancyView.Execution).Reason);
        Assert.Equal(StepReason.BoundsOrAdjacency, rules.CanStep(new()
        {
            LegacyOverride = true
        }, grid.Position(0), new(2, 2, 0), StepPurpose.Transit, OccupancyView.Planning).Reason);
    }

    [Fact]
    public void EveryConflictMatrixRowInBothViews()
    {
        var rows = new (ActorProfile Actor, NavFlags Flags, StepPurpose Purpose, int Planning, int Execution)[]
        {
            (new(), NavFlags.Transit, StepPurpose.Transit, 65, 127),
            (new(), NavFlags.Transit, StepPurpose.Goal, 65, 127),
            (new() { Walkthrough = true }, NavFlags.Transit, StepPurpose.Transit, 32, 32),
            (new() { Walkthrough = true }, NavFlags.Transit, StepPurpose.Goal, 65, 105),
            (new(), NavFlags.Door, StepPurpose.Goal, 32, 32),
            (new(), NavFlags.Transit, StepPurpose.Roller, 127, 127),
            (new() { IgnoreUsers = true }, NavFlags.Transit, StepPurpose.Goal, 32, 32),
            (new() { LegacyOverride = true }, NavFlags.Transit, StepPurpose.Goal, 0, 0),
            (new(), NavFlags.Transit, StepPurpose.Interaction, 0, 0)
        };

        foreach (var row in rows)
        {
            foreach (var view in Enum.GetValues<OccupancyView>())
            {
                var mask = ClaimMatrix.BlockingMask(row.Actor, row.Flags, row.Purpose, view);
                Assert.Equal(view == OccupancyView.Planning ? row.Planning : row.Execution, (int)mask);

                for (var bit = 1; bit <= 64; bit <<= 1)
                {
                    Assert.Equal(((view == OccupancyView.Planning ? row.Planning : row.Execution) & bit) != 0, (mask & (TargetOccupancy)bit) != 0);
                }
            }
        }
    }
    [Fact]
    public void RollerUsesStructuralLegacyFloorStatusAndSkipsHeight()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 1);
        inputs.Publish(NavTest.Record(1, 1, [1], h: 9, walkable: false));
        compiler.ApplyNow();
        var rules = new MovementRules(grid, new());
        Assert.False(rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Roller, OccupancyView.Execution).Ok);
        grid.FloorStatusOverrides[1] = 2;
        inputs.MarkDirty(1);
        compiler.ApplyNow();
        Assert.True(rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Roller, OccupancyView.Execution).Ok);
        Assert.False(rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Goal, OccupancyView.Execution).Ok);
        grid.FloorStatusOverrides[1] = 0;
        inputs.MarkDirty(1);
        compiler.ApplyNow();
        Assert.False(rules.CanStep(new(), grid.Position(0), grid.Position(1), StepPurpose.Roller, OccupancyView.Execution).Ok);
    }
}
