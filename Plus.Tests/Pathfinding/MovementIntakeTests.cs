using System.Reflection;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Tests.Performance;
using Xunit;

namespace Plus.Tests.Pathfinding;

[Collection("Pathfinding room adapter")]
public class MovementIntakeTests
{
    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.Shadow)]
    [InlineData(PathfindingEngine.V2)]
    public void MoveToDefersGoalWritesOnlyForTheV2Executor(PathfindingEngine engine)
    {
        var actor = CreateActor(engine);
        var before = (actor.GoalX, actor.GoalY, actor.PathRecalcNeeded);
        actor.MoveTo(3, 2);
        Assert.Equal(engine == PathfindingEngine.V2 ? before : (3, 2, true),
            (actor.GoalX, actor.GoalY, actor.PathRecalcNeeded));
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.Shadow)]
    [InlineData(PathfindingEngine.V2)]
    public void SetPosDefersPhysicalWritesOnlyForTheV2Executor(PathfindingEngine engine)
    {
        var actor = CreateActor(engine);
        var before = (actor.X, actor.Y, actor.Z);
        actor.SetPos(2, 2, 1.5004);
        Assert.Equal(engine == PathfindingEngine.V2 ? before : (2, 2, 1.5004),
            (actor.X, actor.Y, actor.Z));
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.Shadow)]
    [InlineData(PathfindingEngine.V2)]
    public void ClearMovementDefersCancellationOnlyForTheV2Executor(PathfindingEngine engine)
    {
        var actor = CreateActor(engine); actor.IsWalking = true;
        actor.SetStatus("mv", "2,1,0");
        actor.ClearMovement(true);
        Assert.Equal(engine == PathfindingEngine.V2, actor.IsWalking);
        Assert.Equal(engine == PathfindingEngine.V2, actor.Statusses.ContainsKey("mv"));
    }

    private static RoomUser CreateActor(PathfindingEngine engine)
    {
        var fixture = RoomPerformanceFixture.Create(0, 1);
        var actor = fixture.Users[0]; actor.ClearMovement(true);
        RoomPerformanceFixture.SetField(fixture.Room, "_roomItemHandling", new RoomItemHandling(fixture.Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        if (engine == PathfindingEngine.Legacy) return actor;
        var navigation = new RoomNavigation(fixture.Room, fixture.Map.StaticModel, new() { Engine = engine }, TestLogging.Navigation, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fixture.Map, navigation);
        return actor;
    }
}
