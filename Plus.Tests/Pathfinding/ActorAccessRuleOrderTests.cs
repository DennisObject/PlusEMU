using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

// Pins today's actor-access behaviour (§16.2) before membership moves behind ActorAccessResolver.
public class ActorAccessRuleOrderTests
{
    private static (NavGrid Grid, MovementRules Rules, PlanningOccupancy Occupancy) GateGrid()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 2);
        inputs.Publish(NavTest.Record(1, 1, [1], interaction: InteractionType.GuildGate));
        compiler.ApplyNow();

        return (grid, new MovementRules(grid, new()), new PlanningOccupancy(4));
    }

    private static StepResult Step(MovementRules rules, NavGrid grid, ActorProfile actor, PlanningOccupancy? occupancy = null)
        => rules.CanStep(actor, grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning, occupancy);

    [Fact]
    public void GuildGateAdmitsOnlyLiveMembers()
    {
        var (grid, rules, _) = GateGrid();
        var actor = new ActorProfile();
        Assert.Equal(StepReason.GateDenied, Step(rules, grid, actor).Reason);
        actor.SetMembership(7, true);
        Assert.True(Step(rules, grid, actor).Ok);
        actor.SetMembership(8, true);
        actor.SetMembership(7, false);
        Assert.Equal(StepReason.GateDenied, Step(rules, grid, actor).Reason);
    }

    [Fact]
    public void GuildGateMembershipIsCheckedBeforeOccupancyButAfterFloorLocks()
    {
        var (grid, rules, occupancy) = GateGrid();
        occupancy.Targets[1] = TargetOccupancy.Stationary;
        var outsider = new ActorProfile();
        var member = new ActorProfile();
        member.SetMembership(7, true);
        Assert.Equal(StepReason.GateDenied, Step(rules, grid, outsider, occupancy).Reason);
        Assert.Equal(StepReason.Occupied, Step(rules, grid, member, occupancy).Reason);
        grid.Flags[1] |= NavFlags.FloorLocked;
        Assert.Equal(StepReason.FloorLocked, Step(rules, grid, outsider, occupancy).Reason);
    }

    [Fact]
    public void LegacyOverrideAndInteractionBypassTheGuildGateCheck()
    {
        var (grid, rules, _) = GateGrid();
        Assert.True(Step(rules, grid, new ActorProfile { LegacyOverride = true }).Ok);
        var interaction = new ActorProfile { Interaction = new(0, 0, 1, 0) };
        Assert.True(rules.CanStep(interaction, grid.Position(0), grid.Position(1), StepPurpose.Interaction, OccupancyView.Execution).Ok);
    }

    [Fact]
    public void GoalResolutionRejectsAGuildGateForNonMembersOnly()
    {
        var (grid, _, _) = GateGrid();
        var outsider = new ActorProfile();
        var member = new ActorProfile();
        member.SetMembership(7, true);
        Assert.Equal(-1, GoalResolver.Resolve(grid, outsider, 1, 0, null).Slot);
        Assert.Equal(1, GoalResolver.Resolve(grid, member, 1, 0, null).Slot);
        Assert.Equal(1, GoalResolver.Resolve(grid, new ActorProfile { LegacyOverride = true }, 1, 0, null).Slot);
    }

    [Fact]
    public void MembershipOfOtherGroupsNeverOpensAGuildGate()
    {
        var (grid, rules, _) = GateGrid();
        var actor = new ActorProfile();
        actor.SetMembership(8, true);
        actor.SetMembership(0, true);
        Assert.Equal(StepReason.GateDenied, Step(rules, grid, actor).Reason);
    }
}
