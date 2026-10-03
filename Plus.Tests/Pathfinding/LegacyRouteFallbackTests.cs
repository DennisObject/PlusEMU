using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Spec 16.11 legacy parity: every legacy failure path is routed into the same fallback.
public partial class PlacedFurniRoomTests
{
    public enum LegacyRejection { BlockedState, LastStep, DoorState, Height, GuildGate }

    [Theory]
    [InlineData(LegacyRejection.BlockedState, false)]
    [InlineData(LegacyRejection.BlockedState, true)]
    [InlineData(LegacyRejection.LastStep, false)]
    [InlineData(LegacyRejection.LastStep, true)]
    [InlineData(LegacyRejection.DoorState, false)]
    [InlineData(LegacyRejection.DoorState, true)]
    [InlineData(LegacyRejection.Height, false)]
    [InlineData(LegacyRejection.Height, true)]
    [InlineData(LegacyRejection.GuildGate, false)]
    [InlineData(LegacyRejection.GuildGate, true)]
    public void LegacyEveryRejectionKindReroutesOrStopsBeforeIt(LegacyRejection kind, bool alternative)
    {
        FallbackModel(alternative ? FallbackAlternative : FallbackDeadEnd);
        if (kind == LegacyRejection.GuildGate) LegacyGuildGateWithoutMembers(3, 1);
        var actor = FallbackActor(FallbackEngine.Legacy, 0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        LegacyApply(kind, 3, 1);
        var trail = FallbackRun(actor, 30);
        Assert.DoesNotContain((3, 1), trail.Select(p => (p.X, p.Y)));
        if (alternative) FallbackAssertStopped(actor, 6, 1);
        else FallbackAssertStopped(actor, 2, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyCornerRejectionReroutesOrStopsBeforeTheDiagonal(bool alternative)
    {
        FallbackModel(alternative ? "x000x\r0000x\rx000x" : "xxxxx\r000xx\rx00xx\rxxxxx");
        var actor = FallbackActor(FallbackEngine.Legacy, 0, 1);
        actor.MoveTo(2, 2); ExecutorTick();
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        _room.GetGameMap().SetFloorStatus(2, 1, 0);
        _room.GetGameMap().SetFloorStatus(1, 2, 0);
        FallbackRun(actor, 20);
        if (alternative) FallbackAssertStopped(actor, 2, 2);
        else FallbackAssertStopped(actor, 1, 1);
    }

    [Fact]
    public void LegacyOccupiedGoalStopsOnTheLastTransitTile()
    {
        FallbackModel(FallbackDeadEnd);
        var actor = FallbackActor(FallbackEngine.Legacy, 0, 1);
        actor.MoveTo(6, 1); ExecutorTick(); ExecutorTick();
        FallbackBot(FallbackEngine.Legacy, 6, 1, 2);
        FallbackRun(actor, 30);
        FallbackAssertStopped(actor, 5, 1);
        Assert.Equal((6, 1), (actor.GoalX, actor.GoalY));
    }

    [Fact]
    public void LegacyTruncatedPrefixIsInstalledAsAReversedPathFromTheCurrentTile()
    {
        var (actor, _) = LegacyTruncatedBehindWalker();
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        Assert.Equal(new Vector2D[] { new(5, 1), new(4, 1), new(3, 1), new(2, 1), new(1, 1) }, actor.Path);
        Assert.Equal(1, actor.PathStep);
        Assert.True(actor.IsWalking);
        Assert.False(actor.HasStatus("mv"));
        Assert.Equal((7, 1), (actor.GoalX, actor.GoalY));
    }

    [Fact]
    public void LegacyGateReopeningNeverExtendsTheTruncation()
    {
        var (actor, walker) = LegacyTruncatedBehindWalker();
        FallbackSetGate(_room.GetRoomItemHandler().GetItem(20)!, open: true);
        LegacyWalkerLeaves(walker);
        var trail = FallbackRun(actor, 20);
        Assert.Contains((2, 1), trail.Select(p => (p.X, p.Y)));
        FallbackAssertStopped(actor, 5, 1);
    }

    [Fact]
    public void LegacySecondBlockageInsideThePrefixShortensTheTruncation()
    {
        var (actor, walker) = LegacyTruncatedBehindWalker();
        Add(21, 4, 1, height: 1, stackable: false);
        LegacyWalkerLeaves(walker);
        FallbackRun(actor, 20);
        Assert.Equal(RouteState.Truncated, actor.Movement.Fallback.State);
        FallbackAssertStopped(actor, 3, 1);
    }

    [Fact]
    public void LegacyWaitingBehindAWalkerThatNeverLeavesIsBounded()
    {
        var (actor, _) = LegacyTruncatedBehindWalker();
        var trail = FallbackRun(actor, 20);
        Assert.All(trail, p => Assert.Equal((1, 1), (p.X, p.Y)));
        FallbackAssertStopped(actor, 1, 1);
    }

    [Fact]
    public void LegacyPureStepValidationReportsRejectionsWithoutTouchingTheRoute()
    {
        FallbackModel("x0000\r00000\r00000");
        var map = _room.GetGameMap();
        var user = FallbackActor(FallbackEngine.Legacy, 1, 1);
        user.Path.AddRange([new(3, 1), new(2, 1), new(1, 1)]);
        Assert.Equal(LegacyStepRejection.None, map.IsValidStepPure(user, new(1, 1), new(2, 1), false, false).Rejection);
        Assert.Equal(LegacyStepRejection.NotAdjacent, map.IsValidStepPure(user, new(1, 1), new(3, 1), false, false).Rejection);
        Assert.Equal(LegacyStepRejection.InvalidTile, map.IsValidStepPure(user, new(1, 1), new(9, 1), false, false).Rejection);
        map.SetFloorStatus(2, 1, 0);
        Assert.Equal(LegacyStepRejection.BlockedState, map.IsValidStepPure(user, new(1, 1), new(2, 1), false, false).Rejection);
        map.SetFloorStatus(1, 2, 0);
        Assert.Equal(LegacyStepRejection.Corner, map.IsValidStepPure(user, new(1, 1), new(2, 2), false, false).Rejection);
        map.Model.SqFloorHeight[2, 0] = 2;
        Assert.Equal(LegacyStepRejection.Height, map.IsValidStepPure(user, new(1, 1), new(2, 0), false, false).Rejection);
        FallbackBot(FallbackEngine.Legacy, 0, 1, 2);
        Assert.Equal(LegacyStepRejection.OccupiedGoal, map.IsValidStepPure(user, new(1, 1), new(0, 1), true, false).Rejection);
        Assert.Equal(3, user.Path.Count);
        Assert.False(user.PathRecalcNeeded);
    }

    [Fact]
    public void LegacyPureGuildGateValidationNeitherOpensTheGateNorClearsTheRoute()
    {
        FallbackModel("x0000\r00000\r00000");
        var gate = LegacyGuildGateWithoutMembers(2, 1);
        var map = _room.GetGameMap();
        var user = FallbackActor(FallbackEngine.Legacy, 1, 1);
        user.Path.AddRange([new(2, 1), new(1, 1)]);
        Assert.Equal(LegacyStepRejection.GuildGateDenied, map.IsValidStepPure(user, new(1, 1), new(2, 1), true, false).Rejection);
        Assert.Equal(2, user.Path.Count);
        LegacyGroupMembers().Add(7);
        var check = map.IsValidStepPure(user, new(1, 1), new(2, 1), true, false);
        Assert.True(check.Ok);
        Assert.Same(gate, check.Gate);
        Assert.Equal("0", gate.LegacyDataString);
        Assert.True(map.IsValidStep2(user, new(1, 1), new(2, 1), true, false));
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void LegacyPrefixViewIgnoresWalkingMembersButNotStationaryOnes()
    {
        FallbackModel("x0000\r00000\r00000");
        var map = _room.GetGameMap();
        var user = FallbackActor(FallbackEngine.Legacy, 1, 1);
        var other = FallbackBot(FallbackEngine.Legacy, 2, 1, 2);
        map.GenerateMaps();
        Assert.Equal(LegacyStepRejection.BlockedState, map.IsValidStepPure(user, new(1, 1), new(2, 1), false, false).Rejection);
        other.IsWalking = true;
        Assert.True(map.IsValidStepPure(user, new(1, 1), new(2, 1), false, false, LegacyStepView.Prefix).Ok);
        other.IsWalking = false;
        Assert.Equal(LegacyStepRejection.Occupied,
            map.IsValidStepPure(user, new(1, 1), new(2, 1), false, false, LegacyStepView.Prefix).Rejection);
    }

    [Fact]
    public void LegacyEveryAcceptedMoveToTakesANewCommandSequence()
    {
        FallbackModel(FallbackDeadEnd);
        var actor = FallbackActor(FallbackEngine.Legacy, 0, 1);
        actor.MoveTo(6, 1);
        var first = actor.Movement.Fallback.CommandSequence;
        actor.MoveTo(6, 1);
        Assert.True(actor.Movement.Fallback.CommandSequence > first);
    }

    [Fact]
    public void LegacyRoutePathInstallsAPrefixAsTheReversedListWithAStartSentinel()
    {
        FallbackModel(FallbackDeadEnd);
        var actor = FallbackActor(FallbackEngine.Legacy, 1, 1);
        actor.Path = [new(6, 1), new(5, 1), new(4, 1), new(3, 1), new(2, 1), new(1, 1)]; actor.PathStep = 3;
        LegacyRoutePath.InstallPrefix(actor, [new(2, 1), new(3, 1)]);
        Assert.Equal(new Vector2D[] { new(3, 1), new(2, 1), new(1, 1) }, actor.Path);
        Assert.Equal(1, actor.PathStep);
        Assert.Equal(new Vector2D(2, 1), actor.Path[actor.Path.Count - actor.PathStep - 1]);
        LegacyRoutePath.InstallPrefix(actor, []);
        Assert.Equal(new Vector2D[] { new(1, 1) }, actor.Path);
        Assert.True(actor.PathStep >= actor.Path.Count);
    }

    // Truncates behind a frozen, walking user marked on (2,1); the gate at (6,1) is closed.
    // (2,2) holds the pad the walker leaves to.
    private (RoomUser Actor, RoomUser Walker) LegacyTruncatedBehindWalker()
    {
        FallbackModel("xxxxxxxx\r00000000\rxx0xxxxx");
        var gate = FallbackGate(20, 6, 1);
        ExecutorFloor(25, 2, 2);
        var actor = FallbackActor(FallbackEngine.Legacy, 0, 1);
        actor.MoveTo(7, 1); ExecutorTick();
        var walker = FallbackBot(FallbackEngine.Legacy, 2, 1, 2);
        walker.IsWalking = true; walker.Freezed = true;
        _room.GetGameMap().GenerateMaps();
        FallbackSetGate(gate, open: false);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        return (actor, walker);
    }

    // Leaves atomically: a legacy walk would let the actor's announce mark the still-occupied tile.
    private void LegacyWalkerLeaves(RoomUser walker)
        => _room.GetGameMap().TeleportToItem(walker, _room.GetRoomItemHandler().GetItem(25)!);

    private void LegacyApply(LegacyRejection kind, int x, int y)
    {
        var map = _room.GetGameMap();
        switch (kind)
        {
            case LegacyRejection.BlockedState: map.SetFloorStatus(x, y, 0); break;
            case LegacyRejection.LastStep: map.SetFloorStatus(x, y, 2); break;
            case LegacyRejection.DoorState: map.SetFloorStatus(x, y, 3); break;
            case LegacyRejection.Height: map.Model.SqFloorHeight[x, y] = 2; break;
        }
    }

    private Item LegacyGuildGateWithoutMembers(int x, int y)
    {
        var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
        typeof(Group).GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(group, new List<int>());
        typeof(Group).GetField("_administrators", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(group, new List<int>());
        _legacyGroup = group;
        var groups = Proxy<IGroupManager>((method, args) =>
        {
            Assert.Equal("TryGetGroup", method);
            args[1] = group;
            return true;
        });
        var previous = (IGame)_gameField.GetValue(null)!;
        _gameField.SetValue(null, Proxy<IGame>((method, args) => method == "get_GroupManager"
            ? groups : typeof(IGame).GetMethod(method)!.Invoke(previous, args)));
        var gate = Furni(30, InteractionType.GuildGate, WiredBoxType.None);
        gate.GroupId = 7; gate.ExtraData = new LegacyDataFormat { Data = "0" };
        gate.Definition.Height = 0; gate.Definition.Width = gate.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, gate, x, y, 0, true, false, false));
        return gate;
    }

    private Group? _legacyGroup;

    private List<int> LegacyGroupMembers() => (List<int>)typeof(Group)
        .GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_legacyGroup)!;
}
