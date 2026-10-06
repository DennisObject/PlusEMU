using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Plus.Database;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Quests;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void ExecutorAnnouncesOneStepThenCommitsAndRemovesMvInTheLandingTick()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        var announced = ExecutorUpdate(actor);
        Assert.Equal((0, 1, "0"), (announced.X, announced.Y, announced.Z));
        Assert.Contains("/mv 1,1,0/", announced.Status);
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.IsWalking);
        Assert.DoesNotContain("/mv ", ExecutorUpdate(actor).Status);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void ExecutorFastWalkSkipsIntermediateHooksAndLandsOnce(bool superFast, int startX)
    {
        var origin = ExecutorFloor(10, startX, 1);
        ExecutorFloor(11, startX + 1, 1);
        if (superFast) ExecutorFloor(12, 2, 1);
        var landing = ExecutorFloor(13, 3, 1);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(startX, 1);
        actor.FastWalking = !superFast; actor.SuperFastWalking = superFast;
        actor.MoveTo(3, 1);
        ExecutorTick();
        Assert.Equal(startX, actor.X);
        Assert.Contains("/mv 3,1,0/", ExecutorUpdate(actor).Status);
        Assert.Empty(events);
        ExecutorTick();
        Assert.Equal((3, 1), (actor.X, actor.Y));
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOffFurni, origin.Id),
            (WiredBoxType.TriggerWalkOnFurni, landing.Id) }, events.Select(e => (e.Kind, e.Item)).ToArray());
        Assert.All(events, e => Assert.Equal("3,1,0", e.Mv));
        Assert.DoesNotContain("/mv ", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void ExecutorWalkMagicUsesItsZAndSuppressesRaisedSeatPosture()
    {
        Add(10, 1, 1, z: 5, height: 1, stackable: false);
        Add(11, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        Add(12, 1, 1, z: 7, height: 2, seat: true);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.Contains("/mv 1,1,0.75/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 1, 0.75), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("sit")); Assert.False(actor.HasStatus("lay"));
        Assert.Equal("0.75", ExecutorUpdate(actor).Z);
    }

    [Fact]
    public void ExecutorWalkMagicHeightRebindCorrectsStatusWithoutLandingHooks()
    {
        var tile = Add(10, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        events.Clear();
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(tile, 1, 1, 2.125));
        ExecutorTick();
        Assert.Equal((1, 1, 2.125), (actor.X, actor.Y, actor.Z));
        Assert.Empty(events);
        var correction = ExecutorUpdate(actor);
        Assert.Equal("2.125", correction.Z);
        Assert.DoesNotContain("/mv ", correction.Status);
    }

    [Fact]
    public void ExecutorLockedFirstPendingEdgeCorrectsAtOriginWithoutAnyHooks()
    {
        var origin = ExecutorFloor(10, 0, 1);
        ExecutorFloor(11, 1, 1);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.LastItem = origin;
        actor.MoveTo(1, 1); ExecutorTick();
        _room.GetGameMap().SetFloorStatus(1, 1, 0);
        ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Same(origin, actor.LastItem);
        Assert.Empty(events);
        var correction = ExecutorUpdate(actor);
        Assert.Equal((0, 1), (correction.X, correction.Y));
        Assert.DoesNotContain("/mv ", correction.Status);
    }

    [Fact]
    public void ExecutorFastWalkCommitsOnlyValidPrefixAndFiresOneLandingSequence()
    {
        var origin = ExecutorFloor(10, 0, 1);
        var landing = ExecutorFloor(11, 1, 1);
        ExecutorFloor(12, 2, 1);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1); actor.SuperFastWalking = true;
        actor.MoveTo(3, 1); ExecutorTick();
        _room.GetGameMap().SetFloorStatus(2, 1, 0);
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOffFurni, origin.Id),
            (WiredBoxType.TriggerWalkOnFurni, landing.Id) }, events.Select(e => (e.Kind, e.Item)).ToArray());
        Assert.All(events, e => Assert.Equal("3,1,0", e.Mv));
        Assert.Equal((1, 1), (ExecutorUpdate(actor).X, ExecutorUpdate(actor).Y));
    }

    [Fact]
    public void ExecutorFreezedCommitsPendingKeepsIntentAndResumesAfterElevenCycles()
    {
        _room.GetFreeze();
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        actor.Freezed = true; actor.FreezeCounter = 0;
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.True(actor.Freezed); Assert.False(actor.HasStatus("mv"));
        for (var cycle = 1; cycle < 10; cycle++) ExecutorTick();
        Assert.True(actor.Freezed); Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal((3, 1), (actor.GoalX, actor.GoalY));
        ExecutorTick();
        Assert.False(actor.Freezed);
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));
    }

    [Fact]
    public void ExecutorFrozenRejectsNewClicksButContinuesTheExistingRoute()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        actor.Frozen = true; actor.MoveTo(3, 2);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal((3, 1), (actor.GoalX, actor.GoalY));
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick(); ExecutorTick();
        Assert.Equal((3, 1), (actor.X, actor.Y));
        Assert.True(actor.Frozen); Assert.False(actor.IsWalking);
        Assert.DoesNotContain("/mv ", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void ExecutorUnreachableClickCommitsOldPendingStepThenStays()
    {
        Add(10, 3, 3, height: 2, stackable: false);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        actor.MoveTo(3, 3); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(actor.IsWalking);
        Assert.DoesNotContain("/mv ", ExecutorUpdate(actor).Status);
        ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
    }

    [Fact]
    public void ExecutorMidWalkGoalReplacementCommitsOldStepBeforeAnnouncingNewGoal()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        actor.MoveTo(1, 2); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal((1, 2), (actor.GoalX, actor.GoalY));
        Assert.Contains("/mv 1,2,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 2), (actor.X, actor.Y));
        Assert.False(actor.IsWalking);
        Assert.DoesNotContain("/mv ", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void ExecutorQueuedCancellationDropsTheAnnouncedStepAndLeavesNoGhostLanding()
    {
        ExecutorFloor(10, 1, 1);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(2, 1); ExecutorTick();
        actor.ClearMovement(true);
        Assert.True(actor.HasStatus("mv"));
        ExecutorTick(); ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.IsWalking); Assert.False(actor.HasStatus("mv"));
        Assert.Empty(events);
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(1, 1)));
    }

    [Fact]
    public void ExecutorExactSetPosCancelsPendingAndEscapesFromAnOffGraphStart()
    {
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        actor.SetPos(2, 2, 5.1234);
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        ExecutorTick();
        Assert.Equal((2, 2, 5.1234), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("mv")); Assert.Empty(events);
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(1, 1)));
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        actor.MoveTo(3, 2); ExecutorTick();
        Assert.Contains("/mv 3,2,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((3, 2, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Fact]
    public void ExecutorUpperWalkableSupportDoesNotApplyAnUnrelatedLowerSeatPosture()
    {
        Add(10, 1, 1, z: 0.25, height: 0.5, seat: true);
        ExecutorFloor(11, 1, 1, z: 0.75, height: 0.5);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.Contains("/mv 1,1,1.25/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 1, 1.25), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("sit")); Assert.False(actor.IsSitting);
        Assert.DoesNotContain("/sit ", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void ExecutorSeatArrivalUsesItsSupportBaseAndPostureOffset()
    {
        Add(10, 1, 1, z: 0.25, height: 0.5, seat: true);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.Contains("/mv 1,1,0.25/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 1, 0.25), (actor.X, actor.Y, actor.Z));
        Assert.Equal("0.5", actor.Statusses["sit"]);
        Assert.Contains("/sit 0.5/", ExecutorUpdate(actor).Status);
        Assert.DoesNotContain("/mv ", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void ExecutorDoorArrivalRemovesActorAfterWalkOffAndSkipsWalkOn()
    {
        var origin = ExecutorFloor(10, 1, 1);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(1, 1);
        actor.MoveTo(0, 0); ExecutorTick(); ExecutorTick();
        Assert.Null(_room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId));
        Assert.Null(_client.GetHabbo().CurrentRoom);
        Assert.Contains(ServerPacketHeader.UserRemoveComposer, _client.Sent);
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOffFurni, origin.Id) },
            events.Select(e => (e.Kind, e.Item)).ToArray());
        Assert.False(actor.HasStatus("mv"));
    }

    private RoomUser ExecutorActor(int x, int y, IDatabase? database = null,
        IRewardTrackManager? rewards = null, IGroupManager? groups = null)
    {
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, new() { Engine = PathfindingEngine.V2 },
            TestLogging.Navigation, groups ?? new TestGroupManager(id => _groupLookup(id)),
            database ?? _database, rewards ?? TestNavigationRewards.Instance);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);
        foreach (var item in _room.GetRoomItemHandler().GetFloor) navigation.Inputs.Attach(item);
        var actor = Viewer(x, y); actor.InternalRoomId = actor.VirtualId; actor.UserId = 7;
        navigation.Admit(actor);
        ExecutorTick();
        return actor;
    }

    private Item ExecutorFloor(uint id, int x, int y, double z = 0, double height = 0)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None);
        item.Definition.Walkable = true; item.Definition.Height = height;
        item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, x, y, 0, true, false, false, height: z));
        return item;
    }

    private void ExecutorTick()
    {
        _client.Packets.Clear(); _client.Sent.Clear();
        _room.ProcessRoom();
    }

    private (int X, int Y, string Z, string Status) ExecutorUpdate(RoomUser actor)
    {
        var sent = _client.Packets.Last(packet => packet.Header == ServerPacketHeader.UserUpdateComposer);
        var body = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
        var count = body.ReadInt();
        for (var i = 0; i < count; i++)
        {
            var id = body.ReadInt(); var x = body.ReadInt(); var y = body.ReadInt(); var z = body.ReadString();
            body.ReadInt(); body.ReadInt();
            var status = body.ReadString();
            if (id == actor.VirtualId) return (x, y, z, status);
        }
        throw new InvalidOperationException($"No status for actor {actor.VirtualId}");
    }

    private List<ExecutorWalkEvent> ExecutorWalkEvents()
    {
        var events = new List<ExecutorWalkEvent>();
        ExecutorObserveWalk(900, WiredBoxType.TriggerWalkOffFurni, events);
        ExecutorObserveWalk(901, WiredBoxType.TriggerWalkOnFurni, events);
        return events;
    }

    private void ExecutorObserveWalk(uint id, WiredBoxType kind, List<ExecutorWalkEvent> events)
    {
        var item = Furni(id, InteractionType.WiredTrigger, kind);
        item.Definition.Height = 0; item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));
        Assert.True(_room.GetWired().AddBox(new ExecutorWalkTrigger(_room, item, kind, events)));
    }

    private sealed record ExecutorWalkEvent(WiredBoxType Kind, uint Item, string? Mv);

    private sealed class ExecutorWalkTrigger(Room room, Item item, WiredBoxType kind,
        List<ExecutorWalkEvent> events) : IWiredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => kind;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments)
        {
            var actor = Instance.GetRoomUserManager().GetRoomUserByHabbo(((Plus.HabboHotel.Users.Habbo)arguments[0]).Id);
            events.Add(new(kind, ((Item)arguments[1]).Id, actor.Statusses.GetValueOrDefault("mv")));
            return false;
        }
    }
}
