using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Review fixes for spec 16.11 (PR #36).
public partial class PlacedFurniRoomTests
{
    [Fact]
    public void ExecutorTruncatedPrefixSurvivesTheReplanLimit()
    {
        FallbackModel(FallbackLongDeadEnd);
        var gate = FallbackGate(20, 6, 1);
        var actor = FallbackActor(0, 1, new()
        {
            Engine = PathfindingEngine.V2,
            MaxBlockReplans = 1
        });
        var owner = FallbackBot(3, 3, 2);
        ExecutorTick();
        actor.MoveTo(7, 1);
        ExecutorTick();
        FallbackSetGate(gate, open: false);
        ExecutorTick();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        Assert.Equal(1, actor.Movement.BlockReplans);
        // A roller claim lasts one user phase; the truncated prefix must resume afterwards.
        Assert.True(_room.GetGameMap().Navigation!.Executor.Claims.TryClaim(owner, 1 * 8 + 2, ClaimKind.Roller, TargetOccupancy.None));
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 5, 1);
    }
}
