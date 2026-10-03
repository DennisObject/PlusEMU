using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("stay", 0)]
    [InlineData("nearest", 2)]
    public void GoalResolutionExecutorHonorsTheUnreachablePolicy(string policy, int expectedX)
    {
        var actor = ExecutorConfiguredActor(new() { Engine = PathfindingEngine.V2, UnreachablePolicy = policy });
        _room.GetGameMap().SetFloorStatus(3, 1, 0);
        actor.MoveTo(3, 1);
        for (var cycle = 0; cycle < 8; cycle++) ExecutorTick();
        Assert.Equal((expectedX, 1), (actor.X, actor.Y));
        Assert.False(actor.Movement.HasIntent);
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void GoalResolutionExecutorBudgetCancellationNeverRunsNearestFallback()
    {
        var actor = ExecutorConfiguredActor(new() { Engine = PathfindingEngine.V2,
            UnreachablePolicy = "nearest", MaxExpansionsPerSearch = 1 });
        actor.MoveTo(3, 1);
        for (var cycle = 0; cycle < 8; cycle++) ExecutorTick();
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.False(actor.Movement.HasIntent);
        Assert.False(actor.HasStatus("mv"));
    }
    [Fact]
    public void GoalResolutionExecutorRejectsTheObserversNonWalkableTriggerTile()
    {
        var actor = ExecutorActor(0, 1);
        ExecutorObserveLanding((_, _) => { });
        actor.MoveTo(3, 3); ExecutorTick(); ExecutorTick();
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.False(actor.Movement.HasIntent); Assert.False(actor.HasStatus("mv"));
    }
}
