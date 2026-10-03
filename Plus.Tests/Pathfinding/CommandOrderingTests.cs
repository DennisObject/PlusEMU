using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void CommandIntakeNewClickOnFinalLandingTickSurvivesOldIntentCompletion()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick();
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal((2, 1), (actor.GoalX, actor.GoalY));
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));
    }

    [Fact]
    public void CommandIntakeQueuedCancelCannotDiscardAClickPublishedAfterIt()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick();
        actor.ClearMovement(true);
        actor.MoveTo(0, 2);
        ExecutorTick();
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.Equal((0, 2), (actor.GoalX, actor.GoalY));
        Assert.Contains("/mv 0,2,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((0, 2), (actor.X, actor.Y));
    }
}
