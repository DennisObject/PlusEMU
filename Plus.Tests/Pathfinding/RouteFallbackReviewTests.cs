using System.Reflection;
using Plus.Core.Settings;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Review fixes for spec 16.11 (PR #36).
public partial class PlacedFurniRoomTests
{
    [Fact]
    public void LegacyFallbackDetourIgnoresWalkingUsersOnTheAlternative()
    {
        FallbackModel(FallbackAlternative);
        var gate = FallbackGate(20, 3, 1);
        var pad = ExecutorFloor(25, 5, 1);
        var actor = FallbackActor(FallbackEngine.Legacy, 0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        var walker = FallbackBot(FallbackEngine.Legacy, 3, 3, 2);
        walker.IsWalking = true; walker.Freezed = true;
        FallbackSetGate(gate, open: false);
        var states = new List<RouteState>();
        for (var tick = 0; tick < 2; tick++) { ExecutorTick(); states.Add(actor.Movement.Fallback.State); }
        Assert.Contains(new Vector2D(3, 3), actor.Path);
        // Leaves atomically before the actor arrives (a legacy walk-off would leave a stale tile mark).
        _room.GetGameMap().TeleportToItem(walker, pad);
        for (var tick = 0; tick < 30; tick++) { ExecutorTick(); states.Add(actor.Movement.Fallback.State); }
        Assert.DoesNotContain(RouteState.Truncated, states);
        FallbackAssertStopped(actor, 6, 1);
    }

    [Fact]
    public void LegacyWalkingOnlyBlockerIsWaitedForBeforeTheFallbackSearch()
    {
        var (actor, _) = LegacyBlockedBehindWalker();
        Assert.Equal(RouteState.Normal, actor.Movement.Fallback.State);
        Assert.Equal(1, actor.Movement.WaitTicks);
        Assert.Equal(new Vector2D(7, 1), actor.Path[0]);
        ExecutorTick();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
    }

    [Fact]
    public void ExecutorTruncatedPrefixSurvivesTheReplanLimit()
    {
        FallbackModel(FallbackLongDeadEnd);
        var gate = FallbackGate(20, 6, 1);
        var actor = FallbackActor(FallbackEngine.V2, 0, 1, new() { Engine = PathfindingEngine.V2, MaxBlockReplans = 1 });
        var owner = FallbackBot(FallbackEngine.V2, 3, 3, 2); ExecutorTick();
        actor.MoveTo(7, 1); ExecutorTick();
        FallbackSetGate(gate, open: false);
        ExecutorTick();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        Assert.Equal(1, actor.Movement.BlockReplans);
        // A roller claim lasts one user phase; the truncated prefix must resume afterwards.
        Assert.True(_room.GetGameMap().Navigation!.Executor.Claims.TryClaim(owner, 1 * 8 + 2, ClaimKind.Roller, TargetOccupancy.None));
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 5, 1);
    }

    [Fact]
    public void LegacyStallLimitFollowsTheConfiguredMaxWalkStallTicks()
    {
        using var settings = LegacyFallbackSettings(("pathfinding.max_walk_stall_ticks", "20"));
        var (actor, _) = LegacyTruncatedBehindWalker();
        FallbackRun(actor, 15);
        Assert.True(actor.IsWalking);
        var trail = FallbackRun(actor, 15);
        Assert.All(trail, p => Assert.Equal((1, 1), (p.X, p.Y)));
        FallbackAssertStopped(actor, 1, 1);
    }

    [Fact]
    public void LegacyDeliberateSuppressionResetsTheStallCounter()
    {
        using var settings = LegacyFallbackSettings(("pathfinding.max_walk_stall_ticks", "6"));
        var (actor, _) = LegacyTruncatedBehindWalker();
        FallbackRun(actor, 3);
        actor.Freezed = true;
        FallbackRun(actor, 3);
        Assert.Equal(0, actor.Movement.StallTicks);
        actor.Freezed = false;
        FallbackRun(actor, 4);
        Assert.True(actor.IsWalking);
        FallbackRun(actor, 4);
        FallbackAssertStopped(actor, 1, 1);
    }

    // Installs pathfinding settings for the legacy engine, which snapshots them per room.
    private SettingsScope LegacyFallbackSettings(params (string Key, string Value)[] values)
    {
        var field = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var manager = new SettingsManager(null!, null!);
        typeof(SettingsManager).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, values.ToDictionary(v => v.Key, v => v.Value));
        field.SetValue(null, manager);
        Set("_roomUserManager", new RoomUserManager(_room));
        return new(() => field.SetValue(null, previous));
    }

    private sealed class SettingsScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
