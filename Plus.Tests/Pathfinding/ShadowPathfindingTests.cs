using System.Reflection;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Tests.Performance;
using Xunit;

namespace Plus.Tests.Pathfinding;

[CollectionDefinition("Pathfinding room adapter", DisableParallelization = true)]
public class PathfindingRoomAdapterCollection;

[Collection("Pathfinding room adapter")]
public class ShadowPathfindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealLegacyRecalcHasIdenticalMovementWithShadowEnabled(bool fastWalking)
    {
        var legacy = RoomPerformanceFixture.Create(1, 0);
        var shadow = RoomPerformanceFixture.Create(1, 0);
        var navigation = new RoomNavigation(shadow.Room, shadow.Map.StaticModel,
            new() { Engine = PathfindingEngine.Shadow, ShadowLogSample = 1 });
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shadow.Map, navigation);
        foreach (var fixture in new[] { legacy, shadow })
        {
            var actor = fixture.Bots[0]; actor.GoalX = 3; actor.GoalY = 3;
            actor.FastWalking = fastWalking;
            actor.PathRecalcNeeded = true; actor.IsWalking = false; actor.Path.Clear(); actor.RemoveStatus("mv");
        }
        for (var tick = 0; tick < 4; tick++)
        {
            legacy.Manager.OnCycle(); shadow.Manager.OnCycle();
            var a = legacy.Bots[0]; var b = shadow.Bots[0];
            Assert.Equal((a.X, a.Y, a.Z, a.GoalX, a.GoalY, a.PathStep, a.PathRecalcNeeded, a.IsWalking, a.SetStep),
                (b.X, b.Y, b.Z, b.GoalX, b.GoalY, b.PathStep, b.PathRecalcNeeded, b.IsWalking, b.SetStep));
            Assert.Equal(a.PendingWalkSteps.Select(p => (p.X, p.Y)), b.PendingWalkSteps.Select(p => (p.X, p.Y)));
            Assert.Equal(a.PendingWalkOrigin?.X, b.PendingWalkOrigin?.X);
            Assert.Equal(a.PendingWalkOrigin?.Y, b.PendingWalkOrigin?.Y);
            Assert.Equal(a.PendingWalkConsumesPath, b.PendingWalkConsumesPath);
            Assert.Equal(a.Statusses.OrderBy(p => p.Key), b.Statusses.OrderBy(p => p.Key));
            Assert.Equal(a.Path.Select(p => (p.X, p.Y)), b.Path.Select(p => (p.X, p.Y)));
        }
        Assert.True(navigation.Grid.Version > 0);
    }

    [Fact]
    public void V2SettingCannotActivateAnExecutorInP1()
    {
        var fixture = RoomPerformanceFixture.Create(1, 0);
        var navigation = new RoomNavigation(fixture.Room, fixture.Map.StaticModel, new() { Engine = PathfindingEngine.V2 });
        Assert.False(navigation.Enabled);
    }

    [Fact]
    public void OccupiedGoalsUseActorPolicyAndDoorsRemainShared()
    {
        var (grid, _, _) = NavTest.Create(3, 1, door: 2);
        var occupancy = new PlanningOccupancy(3); occupancy.Targets[1] = occupancy.Targets[2] = TargetOccupancy.Stationary;
        Assert.Equal(-1, GoalResolver.Resolve(grid, new(), 1, 0, occupancy).Slot);
        Assert.Equal(1, GoalResolver.Resolve(grid, new() { IgnoreUsers = true }, 1, 0, occupancy).Slot);
        Assert.Equal(1, GoalResolver.Resolve(grid, new() { LegacyOverride = true }, 1, 0, occupancy).Slot);
        Assert.Equal(2, GoalResolver.Resolve(grid, new(), 2, 0, occupancy).Slot);
        occupancy.Targets[1] = TargetOccupancy.Walking;
        Assert.Equal(-1, GoalResolver.Resolve(grid, new(), 1, 0, occupancy).Slot);
        Assert.Equal(1, GoalResolver.Resolve(grid, new() { Walkthrough = true }, 1, 0, occupancy).Slot);
    }
    [Fact]
    public void InjectedShadowSearchFailureDoesNotChangeMovementInputs()
    {
        var fixture = RoomPerformanceFixture.Create(1, 0);
        var navigation = new RoomNavigation(fixture.Room, fixture.Map.StaticModel,
            new() { Engine = PathfindingEngine.Shadow, ShadowLogSample = 1 });
        navigation.Compiler.RebuildAll();
        navigation.Grid.ActiveNodeCount = -2; // Fault injection: workspace construction fails.
        var actor = fixture.Bots[0]; actor.GoalX = actor.GoalY = 3; actor.PathRecalcNeeded = true;
        var before = (actor.X, actor.Y, actor.Z, actor.GoalX, actor.GoalY, actor.PathRecalcNeeded, actor.PathStep, actor.IsWalking);
        var path = actor.Path.ToArray(); var statuses = actor.Statusses.ToArray();
        navigation.Compare(actor, actor.Path, 1);
        Assert.Equal(before, (actor.X, actor.Y, actor.Z, actor.GoalX, actor.GoalY, actor.PathRecalcNeeded, actor.PathStep, actor.IsWalking));
        Assert.Equal(path, actor.Path); Assert.Equal(statuses, actor.Statusses.ToArray());
    }

    [Fact]
    public void BedClicksResolvePillowsAndNearestFreePillowSlot()
    {
        var (grid, inputs, compiler) = NavTest.Create(4, 4);
        inputs.Publish(NavTest.Record(1, 1, [5, 6, 9, 10], h: 1, walkable: false,
            interaction: Plus.HabboHotel.Items.InteractionType.Bed)); compiler.ApplyNow();
        var occupancy = new PlanningOccupancy(16);
        var goal = GoalResolver.ResolveClick(grid, new(), grid.Position(4), 2, 2, occupancy);
        Assert.Equal(5, goal.Slot);
        occupancy.Targets[5] = TargetOccupancy.Stationary;
        Assert.Equal(6, GoalResolver.ResolveClick(grid, new(), grid.Position(4), 2, 2, occupancy).Slot);
        occupancy.Targets[6] = TargetOccupancy.Stationary;
        Assert.Equal(-1, GoalResolver.ResolveClick(grid, new(), grid.Position(4), 2, 2, occupancy).Slot);
        Assert.Equal(10, GoalResolver.Resolve(grid, new() { IgnoreUsers = true }, 2, 2, occupancy).Slot);
    }
    [Fact]
    public void CachedGroupAccessIsClearedWhenActorHasNoHabboIdentity()
    {
        var fixture = RoomPerformanceFixture.Create(1, 0);
        var navigation = new RoomNavigation(fixture.Room, fixture.Map.StaticModel,
            new() { Engine = PathfindingEngine.Shadow, ShadowLogSample = 0 });
        navigation.Compiler.RebuildAll(); navigation.Grid.GroupId[1] = 7;
        var actor = fixture.Bots[0]; actor.NavigationProfile = new(); actor.NavigationProfile.SetMembership(7, true);
        actor.GoalX = actor.GoalY = 3;
        navigation.Compare(actor, actor.Path, 0);
        Assert.False(actor.NavigationProfile.IsMember(7)); Assert.Equal(2, actor.NavigationProfile.CapabilityVersion);
    }
    [Fact]
    public void AlreadyThereMatchesLegacySingleOriginWithoutDivergence()
    {
        var fixture = RoomPerformanceFixture.Create(1, 0);
        var actor = fixture.Bots[0]; actor.GoalX = actor.X; actor.GoalY = actor.Y;
        var legacy = PathFinder.FindPath(actor, true, fixture.Map, new(actor.X, actor.Y), new(actor.GoalX, actor.GoalY));
        var navigation = NavTest.Enable(fixture.Map); navigation.Compiler.RebuildAll();
        var route = new Route(); var grid = navigation.Grid;
        var outcome = new PathSearch(grid, new()).Find(new(new ActorProfile(), grid.Position(grid.Tile(actor.X, actor.Y)), actor.GoalX, actor.GoalY),
            new PathWorkspace(grid.SlotCapacity, grid.ActiveNodeCount), route);
        Assert.Single(legacy); Assert.Equal(PathOutcome.AlreadyThere, outcome);
        Assert.False(RoomNavigation.Diverges(outcome, route.Count, legacy.Count));
        Assert.True(RoomNavigation.Diverges(outcome, route.Count, 0));
    }

}
