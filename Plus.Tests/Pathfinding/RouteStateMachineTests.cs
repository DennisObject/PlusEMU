using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class RouteStateMachineTests
{
    [Fact]
    public void ANewCommandStartsNormalAndOwnsItsSequence()
    {
        var machine = new RouteStateMachine();
        machine.Begin(7);
        Assert.Equal(RouteState.Normal, machine.State);
        Assert.Equal(7, machine.CommandSequence);
        Assert.Empty(machine.Retained);
    }

    [Fact]
    public void OnlyTheFirstFailureOfARouteRequestsTheCoalescedFallbackSearch()
    {
        var machine = Suspected(out var requested);
        Assert.True(requested);
        Assert.False(machine.Suspect(Steps(4), 0));
        Assert.False(machine.Suspect(Steps(2), 0));
        Assert.Equal(RouteState.Suspect, machine.State);
        Assert.Equal(5, machine.Retained.Count);
    }

    [Fact]
    public void AFoundFallbackReturnsToNormalAndALaterFailureSearchesAgain()
    {
        var machine = Suspected(out _);
        machine.Found();
        Assert.Equal(RouteState.Normal, machine.State);
        Assert.Empty(machine.Retained);
        Assert.True(machine.Suspect(Steps(3), 0));
    }

    [Fact]
    public void AFailedFallbackTruncatesAtThePrefixAndNeverSearchesAgain()
    {
        var machine = Suspected(out _);
        machine.Truncate(3);
        Assert.Equal(RouteState.Truncated, machine.State);
        Assert.Equal(new[] { 1, 2, 3 }, machine.Retained.Select(s => s.X).ToArray());
        Assert.False(machine.Suspect(Steps(5), 0));
        Assert.Equal(RouteState.Truncated, machine.State);
    }

    [Fact]
    public void TruncationEndOnlyEverShortens()
    {
        var machine = Suspected(out _);
        machine.Truncate(3);
        machine.Shorten(5);
        Assert.Equal(3, machine.Retained.Count);
        machine.Shorten(2);
        Assert.Equal(new[] { 1, 2 }, machine.Retained.Select(s => s.X).ToArray());
    }

    [Fact]
    public void ConsumedRouteStepsAreDroppedRelativeToTheCapturedRouteIndex()
    {
        var machine = new RouteStateMachine();
        machine.Begin(1);
        machine.Suspect(Steps(5), routeIndex: 2);
        machine.Consume(4);
        Assert.Equal(new[] { 3, 4, 5 }, machine.Retained.Select(s => s.X).ToArray());
        machine.Rebind();
        machine.Consume(1);
        Assert.Equal(new[] { 4, 5 }, machine.Retained.Select(s => s.X).ToArray());
    }

    [Fact]
    public void ANewCommandDiscardsTheOldRouteState()
    {
        var machine = Suspected(out _);
        machine.Truncate(2);
        machine.Begin(9);
        Assert.Equal(RouteState.Normal, machine.State);
        Assert.Equal(9, machine.CommandSequence);
        Assert.Empty(machine.Retained);
        Assert.True(machine.Suspect(Steps(2), 0));
    }

    private static RouteStateMachine Suspected(out bool requested)
    {
        var machine = new RouteStateMachine();
        machine.Begin(1);
        requested = machine.Suspect(Steps(5), 0);

        return machine;
    }

    private static RetainedStep[] Steps(int count) => Enumerable.Range(1, count)
        .Select(x => new RetainedStep(x, 0, x == count ? StepPurpose.Goal : StepPurpose.Transit, 0, 0)).ToArray();
}
