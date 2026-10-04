using System.Collections.Concurrent;
using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Spec 16.11: a blocked route of the same command reroutes, otherwise walks its valid prefix.
public partial class PlacedFurniRoomTests
{
    // Main corridor on row 1. The alternative corridor on row 3 joins it only at both ends.
    private const string FallbackAlternative = "xxxxxxx\r0000000\r0xxxxx0\r0000000";
    private const string FallbackDeadEnd = "xxxxxxx\r0000000\rxxxxxxx\rxxx0xxx";
    private const string FallbackLongDeadEnd = "xxxxxxxx\r00000000\rxxxxxxxx\rxxx0xxxx";

    public enum FallbackObstacle { ClosingGate, PlacedFurniture }

    [Theory]
    [InlineData(FallbackObstacle.ClosingGate, false)]
    [InlineData(FallbackObstacle.ClosingGate, true)]
    [InlineData(FallbackObstacle.PlacedFurniture, false)]
    [InlineData(FallbackObstacle.PlacedFurniture, true)]
    public void BlockageTwoTilesAheadReroutesOrStopsOnTheTileBeforeIt(FallbackObstacle obstacle, bool alternative)
    {
        FallbackModel(alternative ? FallbackAlternative : FallbackDeadEnd);
        var gate = obstacle == FallbackObstacle.ClosingGate ? FallbackGate(20, 3, 1) : null;
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        if (gate != null) FallbackSetGate(gate, open: false);
        else Add(21, 3, 1, height: 1, stackable: false);
        var trail = FallbackRun(actor, 30);
        Assert.DoesNotContain((3, 1), trail.Select(p => (p.X, p.Y)));
        if (alternative) FallbackAssertStopped(actor, 6, 1);
        else FallbackAssertStopped(actor, 2, 1);
    }

    [Fact]
    public void StationaryUserOnTheRouteStopsTheActorBeforeThem()
    {
        FallbackModel(FallbackDeadEnd);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        FallbackBot(4, 1, 2);
        _room.GetGameMap().GenerateMaps();
        var trail = FallbackRun(actor, 30);
        Assert.DoesNotContain((4, 1), trail.Select(p => (p.X, p.Y)));
        FallbackAssertStopped(actor, 3, 1);
    }

    [Fact]
    public void WalkingUserBlockingBrieflyIsWaitedForWithoutTruncation()
    {
        FallbackModel("xxx0xxx\r0000000\rxxx0xxx\rxxx0xxx");
        var actor = FallbackActor(0, 1);
        var walker = FallbackBot(3, 0, 2); ExecutorTick();
        actor.MoveTo(6, 1); ExecutorTick();
        walker.MoveTo(3, 2);
        var states = new List<RouteState>();
        for (var tick = 0; tick < 30; tick++) { ExecutorTick(); states.Add(actor.Movement.Fallback.State); }
        Assert.DoesNotContain(RouteState.Truncated, states);
        Assert.Equal((3, 2), (walker.X, walker.Y));
        FallbackAssertStopped(actor, 6, 1);
    }

    [Fact]
    public void BlockedNextEdgeWithoutAlternativeStopsAtOnce()
    {
        FallbackModel(FallbackDeadEnd);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.True(actor.HasStatus("mv"));
        _room.GetGameMap().SetFloorStatus(2, 1, 0);
        ExecutorTick();
        FallbackAssertStopped(actor, 1, 1);
    }

    [Fact]
    public void RemovedRugIsNotABlockageWhenTheGoalIsIndependentlyBlocked()
    {
        FallbackModel(FallbackDeadEnd);
        var rug = ExecutorFloor(20, 3, 1);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        _room.GetRoomItemHandler().RemoveFurniture(null!, rug.Id);
        Add(21, 6, 1, height: 1, stackable: false);
        FallbackRun(actor, 30);
        FallbackAssertStopped(actor, 5, 1);
    }

    [Fact]
    public void RemovedSupportUnderTheRouteIsWalkedAtItsNewHeight()
    {
        FallbackModel(FallbackDeadEnd);
        ExecutorFloor(20, 2, 1, height: 1);
        var support = ExecutorFloor(21, 3, 1, height: 1);
        ExecutorFloor(22, 4, 1, height: 1);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        _room.GetRoomItemHandler().RemoveFurniture(null!, support.Id);
        Add(23, 6, 1, height: 1, stackable: false);
        var trail = FallbackRun(actor, 30);
        Assert.Contains((3, 1, 0d), trail);
        FallbackAssertStopped(actor, 5, 1);
    }

    [Fact]
    public void NewUnreachableClickDuringAWalkStays()
    {
        FallbackModel(FallbackAlternative);
        Add(20, 6, 3, height: 1, stackable: false);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        actor.MoveTo(6, 3);
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 2, 1);
    }

    [Fact]
    public void SameTargetReclickAfterTruncationIsANewCommand()
    {
        FallbackModel(FallbackDeadEnd);
        var gate = FallbackGate(20, 3, 1);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        FallbackSetGate(gate, open: false);
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 2, 1);
        var truncated = actor.Movement.Fallback.CommandSequence;
        actor.MoveTo(6, 1);
        FallbackRun(actor, 5);
        Assert.True(actor.Movement.Fallback.CommandSequence > truncated);
        FallbackAssertStopped(actor, 2, 1);
        var stayed = actor.Movement.Fallback.CommandSequence;
        FallbackSetGate(gate, open: true);
        actor.MoveTo(6, 1);
        FallbackRun(actor, 20);
        Assert.True(actor.Movement.Fallback.CommandSequence > stayed);
        FallbackAssertStopped(actor, 6, 1);
    }

    [Fact]
    public void ReachableDetourAboveASmallOperatorExpansionCapStillReroutes()
    {
        FallbackModel(FallbackAlternative);
        var actor = FallbackActor(0, 1, new() { Engine = PathfindingEngine.V2, MaxExpansionsPerSearch = 8 });
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Add(20, 3, 1, height: 1, stackable: false);
        var trail = FallbackRun(actor, 40);
        Assert.Contains((3, 3), trail.Select(p => (p.X, p.Y)));
        FallbackAssertStopped(actor, 6, 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FastWalkPartialCommitWalksThePrefixOrReroutes(bool superFast, bool alternative)
    {
        FallbackModel(alternative ? FallbackAlternative : FallbackDeadEnd);
        var actor = FallbackActor(0, 1);
        actor.FastWalking = !superFast; actor.SuperFastWalking = superFast;
        actor.MoveTo(6, 1); ExecutorTick();
        var blocked = superFast ? 3 : 2;
        Assert.Contains($"/mv {blocked},1,0/", ExecutorUpdate(actor).Status);
        _room.GetGameMap().SetFloorStatus(blocked, 1, 0);
        ExecutorTick();
        Assert.Equal((blocked - 1, 1), (actor.X, actor.Y));
        var trail = FallbackRun(actor, 30);
        Assert.DoesNotContain((blocked, 1), trail.Select(p => (p.X, p.Y)));
        if (alternative) FallbackAssertStopped(actor, 6, 1);
        else FallbackAssertStopped(actor, blocked - 1, 1);
    }

    [Fact]
    public void FastWalkTruncatedPrefixFiresOnlyLandingHooks()
    {
        FallbackModel(FallbackLongDeadEnd);
        for (var x = 0; x < 5; x++) ExecutorFloor((uint)(10 + x), x, 1);
        var gate = FallbackGate(20, 5, 1);
        var events = ExecutorWalkEvents();
        var actor = FallbackActor(0, 1);
        actor.SuperFastWalking = true;
        actor.MoveTo(7, 1); ExecutorTick();
        FallbackSetGate(gate, open: false);
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 4, 1);
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOffFurni, 10u), (WiredBoxType.TriggerWalkOnFurni, 13u),
                (WiredBoxType.TriggerWalkOffFurni, 13u), (WiredBoxType.TriggerWalkOnFurni, 14u) },
            events.Select(e => (e.Kind, e.Item)).ToArray());
    }

    [Fact]
    public void ExecutorGateReopeningBeforeThePrefixEndsNeverExtendsTheTruncation()
    {
        FallbackModel(FallbackLongDeadEnd);
        var gate = FallbackGate(20, 6, 1);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(7, 1); ExecutorTick();
        FallbackSetGate(gate, open: false);
        ExecutorTick();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        Assert.True(actor.X < 5);
        FallbackSetGate(gate, open: true);
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 5, 1);
    }

    [Fact]
    public void ExecutorSecondBlockageInsideThePrefixShortensTheTruncation()
    {
        FallbackModel(FallbackLongDeadEnd);
        var gate = FallbackGate(20, 6, 1);
        var actor = FallbackActor(0, 1);
        actor.MoveTo(7, 1); ExecutorTick();
        FallbackSetGate(gate, open: false);
        ExecutorTick();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        Add(21, 4, 1, height: 1, stackable: false);
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 3, 1);
    }

    [Fact]
    public void ExecutorTransientRollerClaimOnThePrefixDoesNotStopBeforeTheHardBlockage()
    {
        FallbackModel(FallbackDeadEnd);
        var actor = FallbackActor(0, 1);
        var owner = FallbackBot(3, 3, 2); ExecutorTick();
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        Add(20, 5, 1, height: 1, stackable: false);
        Assert.True(_room.GetGameMap().Navigation!.Executor.Claims.TryClaim(owner, 1 * 7 + 3, ClaimKind.Roller, TargetOccupancy.None));
        FallbackRun(actor, 30);
        FallbackAssertStopped(actor, 4, 1);
    }

    [Fact]
    public void ExecutorRepeatedFailuresWhileTheFallbackSearchIsQueuedKeepOneJobInPlace()
    {
        FallbackModel("xxxxxxxx\r00000000\rxxxxxxxx\r00000000\r00000000");
        var actor = FallbackActor(0, 1, new() { Engine = PathfindingEngine.V2, MaxExpansionsPerRoomTick = 1 });
        var bots = Enumerable.Range(0, 3).Select(i => FallbackBot(i, 4, 2 + i)).ToArray();
        var owner = FallbackBot(7, 4, 9); ExecutorTick();
        var scheduler = _room.GetGameMap().Navigation!.Executor.Context.Scheduler;
        actor.MoveTo(7, 1); ExecutorTick();
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        Assert.True(_room.GetGameMap().Navigation!.Executor.Claims.TryClaim(owner, 1 * 8 + 2, ClaimKind.Exclusive, TargetOccupancy.None));
        ExecutorTick();
        Assert.Equal(1, actor.Movement.WaitTicks);
        bots[0].MoveTo(0, 3); bots[1].MoveTo(1, 3);
        ExecutorTick();
        Assert.Equal(RouteState.Suspect, actor.Movement.Fallback.State);
        Assert.Equal(new[] { bots[1], actor }, scheduler.Queued.ToArray());
        bots[2].MoveTo(2, 3);
        ExecutorTick();
        Assert.Equal(new[] { actor, bots[2] }, scheduler.Queued.ToArray());
        Assert.Equal(0, actor.Movement.BlockReplans);
        ExecutorTick();
        Assert.Contains(bots[2], scheduler.Queued);
        Assert.Equal(0, actor.Movement.BlockReplans);
    }

    [Fact]
    public void ExecutorWaitingForAnUnstartedFallbackSearchIsNotAStall()
    {
        FallbackModel("xxxxxxxx\r00000000\r0xxxxxx0\r00000000\rxxxxxxxx\r00000000\r00000000");
        var actor = FallbackActor(0, 1,
            new() { Engine = PathfindingEngine.V2, MaxExpansionsPerRoomTick = 1, MaxWalkStallTicks = 3 });
        var bots = Enumerable.Range(0, 5).Select(i => FallbackBot(i + 2, 5, 2 + i)).ToArray();
        // Held bots never stall out, so their queued searches keep the actor's job unstarted.
        foreach (var bot in bots) bot.CanWalk = false;
        ExecutorTick();
        actor.MoveTo(7, 1); ExecutorTick();
        FallbackBot(2, 1, 10);
        foreach (var bot in bots) bot.MoveTo(bot.X, 6);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal(RouteState.Suspect, actor.Movement.Fallback.State);
        for (var tick = 0; tick < 4; tick++)
        {
            ExecutorTick();
            Assert.True(actor.Movement.HasIntent);
            Assert.Equal(0, actor.Movement.StallTicks);
            Assert.Equal((1, 1), (actor.X, actor.Y));
        }
        FallbackRun(actor, 20);
        FallbackAssertStopped(actor, 7, 1);
    }

    [Fact]
    public void ExecutorOnlyACompletedFailedFallbackSearchCountsTowardsMaxBlockReplans()
    {
        FallbackModel(FallbackLongDeadEnd);
        var gate = FallbackGate(20, 6, 1);
        var actor = FallbackActor(0, 1);
        var owner = FallbackBot(3, 3, 2); ExecutorTick();
        var scheduler = _room.GetGameMap().Navigation!.Executor.Context.Scheduler;
        actor.MoveTo(7, 1); ExecutorTick();
        FallbackSetGate(gate, open: false);
        ExecutorTick();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        Assert.Equal(1, actor.Movement.BlockReplans);
        // A persistent claim ignored by the prefix view keeps the next announce failing.
        Assert.True(_room.GetGameMap().Navigation!.Executor.Claims.TryClaim(owner, 1 * 8 + 2, ClaimKind.Exclusive, TargetOccupancy.None));
        for (var tick = 0; tick < 5; tick++)
        {
            ExecutorTick();
            Assert.Equal(1, actor.Movement.BlockReplans);
            Assert.DoesNotContain(actor, scheduler.Queued);
        }
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.True(actor.Movement.HasIntent);
    }

    private void FallbackModel(string heightmap)
    {
        Set("_gamemap", new Gamemap(_room, new RoomModel("fallback", 0, 0, 0, 0, heightmap, 0, 0, false)));
        _room.GetGameMap().GenerateMaps();
    }

    private RoomUser FallbackActor(int x, int y, PathfindingSettings? settings = null)
    {
        var actor = Viewer(x, y); actor.InternalRoomId = actor.VirtualId; actor.UserId = 7;
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, settings ?? new() { Engine = PathfindingEngine.V2 });
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);
        foreach (var item in _room.GetRoomItemHandler().GetFloor) navigation.Inputs.Attach(item);
        navigation.Admit(actor); ExecutorTick();
        return actor;
    }

    private RoomUser FallbackBot(int x, int y, int id)
    {
        var bot = new RoomUser(0, RoomId, id, _room) { X = x, Y = y, InternalRoomId = id, BotData = ProfileBot(false) };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(users.TryAdd(id, bot));
        _room.GetGameMap().Navigation!.Admit(bot);
        return bot;
    }


    private Item FallbackGate(uint id, int x, int y)
    {
        var gate = Furni(id, InteractionType.Gate, WiredBoxType.None);
        gate.ExtraData = new LegacyDataFormat { Data = "1" };
        gate.Definition.Height = 0; gate.Definition.Width = gate.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, gate, x, y, 0, true, false, false));
        return gate;
    }

    private void FallbackSetGate(Item gate, bool open)
    {
        gate.LegacyDataString = open ? "1" : "0";
        _room.GetGameMap().UpdateMapForItem(gate);
    }

    private List<(int X, int Y, double Z)> FallbackRun(RoomUser actor, int ticks)
    {
        var trail = new List<(int X, int Y, double Z)>();
        for (var tick = 0; tick < ticks; tick++) { ExecutorTick(); trail.Add((actor.X, actor.Y, actor.Z)); }
        return trail;
    }

    private static void FallbackAssertStopped(RoomUser actor, int x, int y)
    {
        Assert.Equal((x, y), (actor.X, actor.Y));
        Assert.False(actor.IsWalking);
        Assert.False(actor.HasStatus("mv"));
        Assert.False(actor.Movement.HasIntent);
    }
}
