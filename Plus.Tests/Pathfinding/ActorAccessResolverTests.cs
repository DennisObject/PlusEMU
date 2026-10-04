using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class ActorAccessResolverTests
{
    private static ActorAccessResolver Resolver(Func<int, int, GroupStanding> source) => new(source);

    [Fact]
    public void RefreshOwnsMembershipAndBumpsTheCapabilityVersionOnlyOnChange()
    {
        var standing = GroupStanding.Outsider;
        var resolver = Resolver((_, _) => standing);
        var profile = new ActorProfile();
        resolver.Refresh(profile, 99, [7]);
        Assert.False(profile.Access.IsMember(7)); Assert.Equal(0, profile.Access.CapabilityVersion);
        standing = GroupStanding.Member; resolver.Refresh(profile, 99, [7]);
        Assert.True(profile.Access.IsMember(7)); Assert.Equal(1, profile.Access.CapabilityVersion);
        resolver.Refresh(profile, 99, [7]);
        Assert.Equal(1, profile.Access.CapabilityVersion);
        standing = GroupStanding.Outsider; resolver.Refresh(profile, 99, [7]);
        Assert.False(profile.Access.IsMember(7)); Assert.Equal(2, profile.Access.CapabilityVersion);
        Assert.Equal(profile.Access.CapabilityVersion, profile.CapabilityVersion);
    }

    [Fact]
    public void RefreshPassesTheHabboAndGroupToTheSourceAndExposesGroupIds()
    {
        var calls = new List<(int Group, int Habbo)>();
        var resolver = Resolver((group, habbo) => { calls.Add((group, habbo)); return GroupStanding.Member; });
        var profile = new ActorProfile();
        resolver.Refresh(profile, 99, [7, 9]);
        Assert.Equal(new[] { (7, 99), (9, 99) }, calls);
        Assert.Equal(new[] { 7, 9 }, profile.Access.GroupIds.Order().ToArray());
    }

    [Fact]
    public void RefreshWithoutAHabboClearsListedGroupsWithoutAskingTheSource()
    {
        var resolver = Resolver((_, _) => throw new InvalidOperationException("No identity"));
        var profile = new ActorProfile(); profile.SetMembership(7, true);
        resolver.Refresh(profile, null, [7]);
        Assert.False(profile.Access.IsMember(7)); Assert.Equal(2, profile.Access.CapabilityVersion);
    }

    [Fact]
    public void RefreshLeavesGroupsOutsideTheListedSetUntouched()
    {
        var resolver = Resolver((_, _) => GroupStanding.Outsider);
        var profile = new ActorProfile(); profile.SetMembership(5, true);
        resolver.Refresh(profile, 99, [7]);
        Assert.True(profile.Access.IsMember(5));
    }

    [Theory]
    [InlineData(GroupStanding.Member, true)]
    [InlineData(GroupStanding.Outsider, false)]
    [InlineData(GroupStanding.Unresolved, false)]
    public void StandingFollowsTheSourceAndRefreshTreatsUnresolvedAsOutsider(GroupStanding standing, bool member)
    {
        var resolver = Resolver((_, _) => standing);
        Assert.Equal(standing, resolver.Standing(99, 7));
        var profile = new ActorProfile(); resolver.Refresh(profile, 99, [7]);
        Assert.Equal(member, profile.Access.IsMember(7));
    }

    [Fact]
    public void StandingWithoutAHabboIsUnresolvedAndNeverAsksTheSource()
    {
        var resolver = Resolver((_, _) => throw new InvalidOperationException("No identity"));
        Assert.Equal(GroupStanding.Unresolved, resolver.Standing(null, 7));
    }

    [Fact]
    public void BooleanMembershipSourcesAdaptToStanding()
    {
        var resolver = new ActorAccessResolver((_, habbo) => habbo == 1);
        Assert.Equal(GroupStanding.Member, resolver.Standing(1, 7));
        Assert.Equal(GroupStanding.Outsider, resolver.Standing(2, 7));
    }

    [Fact]
    public void MovementRulesConsultTheResolverForGuildGateStepsFlanksAndGoals()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 2);
        inputs.Publish(NavTest.Record(1, 1, [1], interaction: InteractionType.GuildGate));
        compiler.ApplyNow();
        var allowAll = new AllowAll(); var denyAll = new DenyAll();
        var outsider = new ActorProfile(); var member = new ActorProfile(); member.SetMembership(7, true);
        var open = new MovementRules(grid, new(), allowAll); var closed = new MovementRules(grid, new(), denyAll);
        Assert.True(open.CanStep(outsider, grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning).Ok);
        Assert.Equal(StepReason.GateDenied, closed.CanStep(member, grid.Position(0), grid.Position(1), StepPurpose.Transit, OccupancyView.Planning).Reason);
        Assert.True(open.CanFlank(outsider, grid.Position(0), 1, 0));
        Assert.False(closed.CanFlank(member, grid.Position(0), 1, 0));
        Assert.Equal(1, GoalResolver.Resolve(grid, outsider, 1, 0, null, allowAll).Slot);
        Assert.Equal(-1, GoalResolver.Resolve(grid, member, 1, 0, null, denyAll).Slot);
    }

    [Fact]
    public void DefaultResolverAdmitsExactlyTheCachedMembers()
    {
        var member = new ActorProfile(); member.SetMembership(7, true);
        Assert.True(ActorAccessResolver.Cached.CanEnterGuildGate(member, 7));
        Assert.False(ActorAccessResolver.Cached.CanEnterGuildGate(member, 8));
        Assert.False(ActorAccessResolver.Cached.CanEnterGuildGate(new ActorProfile(), 7));
    }

    private sealed class AllowAll() : ActorAccessResolver((_, _) => GroupStanding.Member)
    {
        public override bool CanEnterGuildGate(ActorProfile actor, int groupId) => true;
    }

    private sealed class DenyAll() : ActorAccessResolver((_, _) => GroupStanding.Member)
    {
        public override bool CanEnterGuildGate(ActorProfile actor, int groupId) => false;
    }
}
