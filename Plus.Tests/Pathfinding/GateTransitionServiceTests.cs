using System.Drawing;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// §16.3: every gate state write goes through one per-gate sequencer on the room task.
public partial class PlacedFurniRoomTests
{
    private Item ClosableGate(InteractionType kind = InteractionType.Gate, int width = 1, string state = "1", uint id = 20)
    {
        var gate = Furni(id, kind, WiredBoxType.None);
        gate.Definition.Height = 0; gate.Definition.Modes = 2; gate.Definition.Walkable = false;
        gate.Definition.Width = width; gate.Definition.Length = 1; gate.GroupId = 7; gate.UserId = 7;
        InitializeNativeState(gate); gate.LegacyDataString = state;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, gate, 1, 1, 0, true, false, false));
        return gate;
    }

    private static Point NonAnchor(Item gate) => gate.GetCoords.First(tile => tile != gate.Coordinate);

    private GateTransitionService Gates => _room.GetGameMap().Gates;

    private GateTransition CloseOnOwner(Item gate, GateCloseReason reason = GateCloseReason.Click)
    {
        using var owner = RoomOwnerScope.Enter(_room);
        return Gates.TryClose(gate, reason, "0", persist: false);
    }

    private void UseGameService(string getter, object service)
    {
        var previous = (IGame)_gameField.GetValue(null)!;
        _gameField.SetValue(null, Proxy<IGame>((method, args) => method == getter ? service
            : typeof(IGame).GetMethod(method)!.Invoke(previous, args)));
    }

    private (RoomUser Actor, RoomNavigation Navigation) ActorOn(Point tile)
    {
        var actor = ExecutorActor(tile.X, tile.Y);
        return (actor, _room.GetGameMap().Navigation!);
    }

    [Fact]
    public void GateCloseRefusesWhenAnOccupantStandsOnANonAnchorFootprintTile()
    {
        var gate = ClosableGate(width: 2);
        var (_, navigation) = ActorOn(NonAnchor(gate));
        _client.Sent.Clear();
        Assert.Equal(GateTransition.Refused, CloseOnOwner(gate));
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal("1", navigation.Inputs.Read(gate.Id)!.State);
        Assert.DoesNotContain(ServerPacketHeader.ObjectUpdateComposer, _client.Sent);
    }

    [Fact]
    public void GateCloseSucceedsOnceTheFootprintIsEmptyAndPublishesBeforeBroadcasting()
    {
        var gate = ClosableGate(width: 2);
        var (actor, navigation) = ActorOn(new Point(0, 2));
        var observed = new List<(string Item, string Record)>();
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.ObjectUpdateComposer)
                observed.Add((gate.LegacyDataString, navigation.Inputs.Read(gate.Id)!.State));
        };
        Assert.Equal(GateTransition.Applied, CloseOnOwner(gate));
        Assert.Equal(new[] { ("0", "0") }, observed);
        Assert.Equal((0, 2), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData(ClaimKind.Exclusive)]
    [InlineData(ClaimKind.Goal)]
    [InlineData(ClaimKind.Shared)]
    [InlineData(ClaimKind.Roller)]
    public void GateCloseRefusesWhileAnyClaimCoversAFootprintTileAndSucceedsAfterRelease(ClaimKind kind)
    {
        var gate = ClosableGate(width: 2);
        var (actor, navigation) = ActorOn(new Point(0, 2));
        var claims = navigation.Executor.Claims;
        var slot = navigation.Grid.Tile(NonAnchor(gate).X, NonAnchor(gate).Y);
        Assert.True(claims.TryClaim(actor, slot, kind, TargetOccupancy.None));
        Assert.Equal(GateTransition.Refused, CloseOnOwner(gate)); Assert.Equal("1", gate.LegacyDataString);
        claims.Release(actor);
        Assert.Equal(GateTransition.Applied, CloseOnOwner(gate)); Assert.Equal("0", gate.LegacyDataString);
    }

    [Fact]
    public void GateCloseRefusesForAnOffGraphMember()
    {
        var gate = ClosableGate(width: 2);
        var (actor, navigation) = ActorOn(new Point(0, 2));
        var tile = navigation.Grid.Tile(NonAnchor(gate).X, NonAnchor(gate).Y);
        navigation.Executor.Claims.Move(actor, null, tile, false, actor.Movement.LifetimeId);
        Assert.Equal(GateTransition.Refused, CloseOnOwner(gate)); Assert.Equal("1", gate.LegacyDataString);
        navigation.Executor.Claims.Move(actor, navigation.Grid.Tile(0, 2), navigation.Grid.Tile(0, 2), false, actor.Movement.LifetimeId);
        Assert.Equal(GateTransition.Applied, CloseOnOwner(gate));
    }

    [Fact]
    public void GateCloseRefusesForAWalkingCommittedMember()
    {
        var gate = ClosableGate();
        var (actor, navigation) = ActorOn(new Point(0, 1));
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.Equal(GateTransition.Refused, CloseOnOwner(gate));
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void GateOpeningIsNeverGuardedOrQueued()
    {
        var gate = ClosableGate(state: "0");
        ActorOn(gate.Coordinate);
        Assert.Equal(GateTransition.Applied, GateTransitionService.Apply(gate, "1", GateCloseReason.Click, persist: false));
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Theory]
    [InlineData(InteractionType.Gate, "1", "0", true)]
    [InlineData(InteractionType.Gate, "1", "2", true)]
    [InlineData(InteractionType.Gate, "0", "1", false)]
    [InlineData(InteractionType.Gate, "0", "0", false)]
    [InlineData(InteractionType.Gate, "2", "0", false)]
    [InlineData(InteractionType.GuildGate, "1", "0", true)]
    [InlineData(InteractionType.GateVip, "1", "0", true)]
    [InlineData(InteractionType.GateVip, "0", "1", false)]
    [InlineData(InteractionType.None, "1", "0", false)]
    [InlineData(InteractionType.OneWayGate, "1", "0", false)]
    public void GateClosingIsAGateKindLeavingTheOpenState(InteractionType kind, string current, string next, bool closing)
    {
        var item = Furni(30, kind, WiredBoxType.None); item.Definition.Modes = 2;
        InitializeNativeState(item); item.LegacyDataString = current;
        Assert.Equal(closing, GateTransitionService.IsClosing(item, next));
    }

    [Fact]
    public void GateCloseTreatsNonGateFurnitureAsAPlainStateWrite()
    {
        var lamp = ClosableGate(InteractionType.None);
        ActorOn(lamp.Coordinate);
        Assert.Equal(GateTransition.Applied, GateTransitionService.Apply(lamp, "0", GateCloseReason.Click, persist: false));
        Assert.Equal("0", lamp.LegacyDataString);
    }

    [Fact]
    public void GateInteractorClickRefusesAClosingToggleThroughANonAnchorOccupant()
    {
        var gate = ClosableGate(width: 2);
        ActorOn(NonAnchor(gate));
        using (RoomOwnerScope.Enter(_room)) new InteractorGate().OnTrigger(_client, gate, 0, true);
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void GateInteractorClickClosesAndRunsItsFollowUpWhenFree()
    {
        var gate = ClosableGate(width: 2);
        ActorOn(new Point(0, 2));
        var triggered = new List<uint>();
        var observer = Furni(21, InteractionType.WiredTrigger, WiredBoxType.TriggerStateChanges);
        observer.Definition.Height = 0; observer.Definition.Width = observer.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, observer, 3, 3, 0, true, false, false));
        Assert.True(_room.GetWired().AddBox(new GateStateObserver(_room, observer, triggered)));
        using (RoomOwnerScope.Enter(_room)) new InteractorGate().OnTrigger(_client, gate, 0, true);
        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal(new[] { gate.Id }, triggered);
    }

    [Fact]
    public void GateInteractorWiredToggleRefusesWhenOccupiedAndOpensAgainFreely()
    {
        var gate = ClosableGate(width: 2);
        ActorOn(NonAnchor(gate));
        using var owner = RoomOwnerScope.Enter(_room);
        new InteractorGate().OnWiredTrigger(gate);
        Assert.Equal("1", gate.LegacyDataString);
        gate.LegacyDataString = "0";
        new InteractorGate().OnWiredTrigger(gate);
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void GateGenericSwitchInteractorIsGuarded(bool occupied, string expected)
    {
        UseGameService("get_QuestManager", Proxy<IQuestManager>((_, _) => null));
        var gate = ClosableGate(width: 2);
        ActorOn(occupied ? NonAnchor(gate) : new Point(0, 2));
        using (RoomOwnerScope.Enter(_room)) new InteractorGenericSwitch(TestItemRuntime.Quests, TestItemRuntime.Rewards).OnTrigger(_client, gate, 0, true);
        Assert.Equal(expected, gate.LegacyDataString);
    }

    [Fact]
    public void GateGenericSwitchStillTogglesPlainFurnitureThroughOccupants()
    {
        UseGameService("get_QuestManager", Proxy<IQuestManager>((_, _) => null));
        var lamp = ClosableGate(InteractionType.None);
        ActorOn(lamp.Coordinate);
        using (RoomOwnerScope.Enter(_room)) new InteractorGenericSwitch(TestItemRuntime.Quests, TestItemRuntime.Rewards).OnTrigger(_client, lamp, 0, true);
        Assert.Equal("0", lamp.LegacyDataString);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void GateWiredMatchPositionBoxCannotCloseThroughAnOccupant(bool occupied, string expected)
    {
        var gate = ClosableGate(width: 2);
        ActorOn(occupied ? NonAnchor(gate) : new Point(0, 2));
        var box = new MatchPositionBox(_room, Furni(22, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition))
        { StringData = "1;0;0", ItemsData = $"{gate.Id}:1,1,0,0,0" };
        box.SetItems.TryAdd(gate.Id, gate);
        using (RoomOwnerScope.Enter(_room)) Assert.True(box.Execute());
        Assert.Equal(expected, gate.LegacyDataString);
    }

    [Theory]
    [InlineData(InteractionType.GuildGate, 2)]
    [InlineData(InteractionType.GateVip, 1)]
    public void GateAutomaticCloseIsRetainedUntilTheClaimClears(InteractionType kind, int retryCycles)
    {
        var gate = ClosableGate(kind);
        var (actor, navigation) = ActorOn(new Point(0, 1));
        var slot = navigation.Grid.Tile(1, 1);
        Assert.True(navigation.Executor.Claims.TryClaim(actor, slot, ClaimKind.Shared, TargetOccupancy.None));
        using var owner = RoomOwnerScope.Enter(_room);
        gate.RequestUpdate(1, true); gate.ProcessUpdates();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(retryCycles, gate.UpdateCounter);
        navigation.Executor.Claims.Release(actor);
        gate.ProcessUpdates(); gate.ProcessUpdates();
        Assert.Equal("0", gate.LegacyDataString);
    }

    [Fact]
    public void GateOffOwnerRequestsQueueBeforeMutatingAndDrainOnTheRoomTask()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var result = Task.Run(() => Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false)).Result;
        Assert.Equal(GateTransition.Queued, result);
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateOffOwnerExplicitRefusalIsDroppedButAutomaticIsRetriedNextCycle()
    {
        var click = ClosableGate(); var auto = ClosableGate(id: 21);
        var (actor, _) = ActorOn(click.Coordinate);
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(click, GateCloseReason.Click, "0", persist: false)).Result);
        ExecutorTick();
        Assert.Equal("1", click.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(auto, GateCloseReason.Automatic, "0", persist: false)).Result);
        ExecutorTick();
        Assert.Equal("1", auto.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
        actor.MoveTo(0, 1); ExecutorTick(); ExecutorTick(); ExecutorTick();
        Assert.Equal("0", auto.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateOffOwnerCallerNeverMutatesWhileQueued()
    {
        var gate = ClosableGate();
        var applied = new List<string>();
        var result = Task.Run(() => Gates.TryClose(gate, GateCloseReason.Wired, "0", persist: false, afterClose: item => applied.Add(item.LegacyDataString))).Result;
        Assert.Equal(GateTransition.Queued, result); Assert.Empty(applied);
        using (RoomOwnerScope.Enter(_room)) Gates.Drain();
        Assert.Equal(new[] { "0" }, applied);
    }

    [Fact]
    public void GateQueuedCloseIsDroppedWhenTheGateLeftTheRoom()
    {
        var gate = ClosableGate();
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(gate, GateCloseReason.Automatic, "0", persist: false)).Result);
        _room.GetRoomItemHandler().RemoveFurniture(_client, gate.Id);
        using (RoomOwnerScope.Enter(_room)) Gates.Drain();
        Assert.Equal(0, Gates.PendingCount); Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void GateLegacyAutomaticCloseKeepsItsRetryWhileAUserStandsOnTheGate()
    {
        var gate = ClosableGate(InteractionType.GuildGate);
        var user = Viewer(1, 1); _room.GetGameMap().AddUserToMap(user, gate.Coordinate);
        using var owner = RoomOwnerScope.Enter(_room);
        gate.RequestUpdate(1, true); gate.ProcessUpdates();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(2, gate.UpdateCounter);
        _room.GetGameMap().RemoveUserFromMap(user, gate.Coordinate);
        gate.ProcessUpdates(); gate.ProcessUpdates();
        Assert.Equal("0", gate.LegacyDataString);
    }

    private sealed class GateStateObserver(Room room, Item item, List<uint> seen) : IWiredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.TriggerStateChanges;
        public System.Collections.Concurrent.ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments)
        {
            seen.Add(((Item)arguments[1]).Id); return false;
        }
    }

    [Fact]
    public void GateV2MemberOpenedGuildGateStaysOpenWhileTheMemberStandsOnItAndClosesAfterDeparture()
    {
        var gate = ReviewGuildGate(member: true);
        var actor = ReviewGateActor(true);
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        for (var cycle = 0; cycle < 10; cycle++) ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y)); Assert.Equal("1", gate.LegacyDataString);
        Assert.True(gate.UpdateCounter > 0);
        actor.MoveTo(2, 1);
        for (var cycle = 0; cycle < 10; cycle++) ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString);
    }

    private sealed class ProbeOccupancy(Action<IReadOnlyList<Point>> onQuery) : IGateOccupancy
    {
        public bool IsBlocked(IReadOnlyList<Point> footprint) { onQuery(footprint); return false; }
    }

    private GateTransition CloseWith(GateTransitionService service, Item gate)
    {
        using var owner = RoomOwnerScope.Enter(_room);
        return service.TryClose(gate, GateCloseReason.Click, "0", persist: false);
    }

    [Fact]
    public void GateCloseValidatesUnderPlacementSyncWithTheNavigationLockOrderedAfterIt()
    {
        var gate = ClosableGate(width: 2);
        var map = _room.GetGameMap(); var held = new List<(bool Placement, bool Nav)>();
        var service = new GateTransitionService(_room, () => new ProbeOccupancy(_ =>
            held.Add((Monitor.IsEntered(map.PlacementSync), Monitor.IsEntered(gate.NavSync)))));
        Assert.Equal(GateTransition.Applied, CloseWith(service, gate));
        Assert.Equal(new[] { (true, false) }, held);
        Assert.Equal("0", gate.LegacyDataString);
    }

    [Fact]
    public void GateCloseBroadcastsAndRunsCallbacksOutsideEveryLock()
    {
        var gate = ClosableGate(width: 2); var map = _room.GetGameMap(); ActorOn(new Point(0, 2));
        var observed = new List<(string Where, bool Placement, bool Nav)>();
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.ObjectUpdateComposer)
                observed.Add(("broadcast", Monitor.IsEntered(map.PlacementSync), Monitor.IsEntered(gate.NavSync)));
        };
        using (RoomOwnerScope.Enter(_room))
            Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false,
                afterClose: _ => observed.Add(("after", Monitor.IsEntered(map.PlacementSync), Monitor.IsEntered(gate.NavSync))));
        Assert.Equal(new[] { ("broadcast", false, false), ("after", false, false) }, observed);
    }

    [Fact]
    public void GateCloseKeepsAPacketThreadMoveFromRelocatingTheGateMidTransaction()
    {
        var gate = ClosableGate(width: 2); var map = _room.GetGameMap();
        Task? mover = null; var movedDuringValidation = true; IReadOnlyList<Point>? validated = null;
        var service = new GateTransitionService(_room, () => new ProbeOccupancy(footprint =>
        {
            validated = footprint.ToArray();
            mover = Task.Run(() => _room.GetRoomItemHandler().SetFloorItem(gate, 3, 3, 0));
            Thread.Sleep(150); movedDuringValidation = mover.IsCompleted;
        }));
        Assert.Equal(GateTransition.Applied, CloseWith(service, gate));
        Assert.False(movedDuringValidation);
        Assert.True(mover!.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { new Point(1, 1), new Point(2, 1) }, validated!.OrderBy(p => p.X).ToArray());
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal((3, 3), (gate.GetX, gate.GetY));
        Assert.Equal("0", gate.NavigationInputs is null ? "0" : map.Navigation!.Inputs.Read(gate.Id)!.State);
    }

    [Fact]
    public void GateFastWiredPassRunsOnTheRoomOwnerSoConsecutiveTogglesAlternate()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2)); var states = new List<string>();
        _room.RunFastPass(() =>
        {
            new InteractorGate().OnWiredTrigger(gate); states.Add(gate.LegacyDataString);
            new InteractorGate().OnWiredTrigger(gate); states.Add(gate.LegacyDataString);
        });
        Assert.Equal(new[] { "0", "1" }, states);
        Assert.Equal(0, Gates.PendingCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GateMannequinPacketsRejectNonMannequinItemsBeforeMutating(bool figure)
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2)); _client.GetHabbo().Gender = "M"; _client.GetHabbo().Look = "hd-180-1.ch-210-66"; _client.GetHabbo().Clothing = new();
        var packet = figure ? ClientPacket((int)gate.Id) : ClientPacket((int)gate.Id, "renamed");
        var metadata = new RoomItemMetadataService(Proxy<IRoomItemMetadataStore>((_, _) => null), Proxy<Plus.Core.FigureData.IFigureDataManager>((_, args) => args[0]));
        IPacketEvent handler = figure ? new SetMannequinFigureEvent(metadata) : new SetMannequinNameEvent(metadata);
        handler.Parse(_client, packet).Wait();
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GateMannequinPacketsStillUpdateRealMannequins(bool figure)
    {
        var mannequin = ClosableGate(InteractionType.Mannequin, state: $"m{(char)5}.ch-1{(char)5}Default");
        _client.GetHabbo().Gender = "F"; _client.GetHabbo().Look = "hd-180-1.ch-210-66"; _client.GetHabbo().Clothing = new();
        var packet = figure ? ClientPacket((int)mannequin.Id) : ClientPacket((int)mannequin.Id, "renamed");
        var metadata = new RoomItemMetadataService(Proxy<IRoomItemMetadataStore>((_, _) => null), Proxy<Plus.Core.FigureData.IFigureDataManager>((_, args) => args[0]));
        IPacketEvent handler = figure ? new SetMannequinFigureEvent(metadata) : new SetMannequinNameEvent(metadata);
        handler.Parse(_client, packet).Wait();
        Assert.NotEqual($"m{(char)5}.ch-1{(char)5}Default", mannequin.LegacyDataString);
    }

    private void ClickFromPacketThread(Item gate)
        => Task.Run(() => new InteractorGate().OnTrigger(_client, gate, 0, true)).Wait();

    [Fact]
    public void GateTwoPacketThreadClicksInOneTickLeaveAnOpenGateOpen()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
        ClickFromPacketThread(gate);
        Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateThreePacketThreadClicksInOneTickCloseTheGate()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate); ClickFromPacketThread(gate); ClickFromPacketThread(gate);
        Assert.Equal(3, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateClickOnAClosedGateStillOpensImmediatelyAndTheNextClickQueuesTheClose()
    {
        var gate = ClosableGate(state: "0"); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
        ClickFromPacketThread(gate);
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString);
    }

    [Fact]
    public void GateGenericSwitchTogglesQueueInOrder()
    {
        UseGameService("get_QuestManager", Proxy<IQuestManager>((_, _) => null));
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        Task.Run(() => new InteractorGenericSwitch(TestItemRuntime.Quests, TestItemRuntime.Rewards).OnTrigger(_client, gate, 0, true)).Wait();
        Assert.Equal(1, Gates.PendingCount);
        Task.Run(() => new InteractorGenericSwitch(TestItemRuntime.Quests, TestItemRuntime.Rewards).OnTrigger(_client, gate, 0, true)).Wait();
        Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void GateWiredToggleOnTheOwnerQueuesBehindAPacketThreadClose()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        Assert.Equal(1, Gates.PendingCount);
        using (RoomOwnerScope.Enter(_room)) new InteractorGate().OnWiredTrigger(gate);
        Assert.Equal(2, Gates.PendingCount); Assert.Equal("1", gate.LegacyDataString);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void GateQueuedAutomaticCloseIsFollowedByAClickToggleAgainstCommittedState()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(gate, GateCloseReason.Automatic, "0", persist: false)).Result);
        ClickFromPacketThread(gate);
        Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateRepeatedExplicitCloseRequestsQueueInOrder()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        Task.Run(() => Gates.TryClose(gate, GateCloseReason.Wired, "0", persist: false)).Wait();
        Task.Run(() => Gates.TryClose(gate, GateCloseReason.Wired, "0", persist: false)).Wait();
        Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    private static string? Flip(string state) => state == "1" ? "0" : "1";

    [Fact]
    public void GateDrainCommitIsAtomicWithAToggleSoADoubleClickNeverDoubleCloses()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        using var inCommit = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var reached = new ManualResetEventSlim();
        var service = new GateTransitionService(_room, () => new ProbeOccupancy(_ =>
        { inCommit.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }));
        Assert.Equal(GateTransition.Queued, Task.Run(() => service.Toggle(gate, Flip, GateCloseReason.Click, persist: false)).Result);
        var drain = Task.Run(() => { using var owner = RoomOwnerScope.Enter(_room); service.Drain(); });
        try
        {
            Assert.True(inCommit.Wait(TimeSpan.FromSeconds(5)));
            Thread? contender = null;
            service.DecisionHook = () => { contender = Thread.CurrentThread; reached.Set(); };
            var click = Task.Run(() => service.Toggle(gate, Flip, GateCloseReason.Click, persist: false));
            Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
            // Reaching the hook proves the contender is at its decision; it may only finish once the commit lock is free.
            var settled = SpinWait.SpinUntil(() => click.IsCompleted || (contender!.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5));
            Assert.True(settled);
            var finishedWhileCommitting = click.IsCompleted;
            release.Set();
            Assert.True(Task.WhenAll(drain, click).Wait(TimeSpan.FromSeconds(5)));
            Assert.False(finishedWhileCommitting);
            // It decides only after the commit: appended behind the still-busy close, or applied once that close has ended.
            Assert.True(click.Result is GateTransition.Queued or GateTransition.Applied);
        }
        finally { release.Set(); }
        using (RoomOwnerScope.Enter(_room)) service.Drain();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, service.PendingCount);
    }

    private sealed class NoWiredOperations : Plus.HabboHotel.Items.Wired.Runtime.IWiredRuntimeOperations
    {
        public bool CallStacks(Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext context, IEnumerable<Item> receivers, Plus.HabboHotel.Items.Wired.Runtime.WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    private Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction ToggleAction(Item gate, out Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext context)
    {
        var box = Furni(40, InteractionType.WiredEffect, WiredBoxType.None);
        var action = new Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction(_room, box,
            Plus.HabboHotel.Items.Wired.Configuration.WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_toggle_state"),
            new(), _ => { }, (_, _, _) => { }, new(), TestLogging.Logger, TimeProvider.System, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestWiredDefinitions.Unused, TestItemRuntime.Travel);
        Assert.True(action.TryValidateConfiguration(new() { IntParams = [0, 100], SelectedItems = [gate.Id] }, out var config, out var error), error);
        action.ApplyConfiguration(config);
        var items = _room.GetRoomItemHandler().GetFloor.ToArray(); var users = _room.GetRoomUserManager().GetUserList().ToArray();
        context = new(_room, new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Use), new(() => items, () => users), new NoWiredOperations());
        return action;
    }

    [Fact]
    public void GateModernToggleAfterAPacketCloseQueuesBehindItAndTheGateEndsOpen()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        var action = ToggleAction(gate, out var context);
        ClickFromPacketThread(gate);
        Assert.Equal(1, Gates.PendingCount);
        _room.RunFastPass(() => Assert.True(action.Execute(context)));
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateModernToggleOnAPlainOpenGateStillClosesImmediatelyOnTheFastPass()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        var action = ToggleAction(gate, out var context);
        _room.RunFastPass(() => Assert.True(action.Execute(context)));
        Assert.Equal("0", gate.LegacyDataString);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GateAbsoluteOpeningWriteQueuesBehindACloseFromAnyThread(bool onOwner)
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        Assert.Equal(1, Gates.PendingCount);
        Func<GateTransition> open = () => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false);
        var result = onOwner ? RunOwner(open) : Task.Run(open).Result;
        Assert.Equal(GateTransition.Queued, result); Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    private T RunOwner<T>(Func<T> work) { using var owner = RoomOwnerScope.Enter(_room); return work(); }

    [Fact]
    public void GateAbsoluteCloseWriteOnAGateWithAQueuedCloseQueuesInOrder()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.Apply(gate, "0", GateCloseReason.Wired, persist: false)).Result);
        Assert.Equal(2, Gates.PendingCount);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    private sealed class BlockedOccupancy(Action onQuery) : IGateOccupancy
    {
        public bool IsBlocked(IReadOnlyList<Point> footprint) { onQuery(); return true; }
    }

    private void DrainOnOwner() { using var owner = RoomOwnerScope.Enter(_room); Gates.Drain(); }

    [Fact]
    public void GateSequencedMultiStateGateTogglesTwiceThroughEveryStateInOrder()
    {
        var gate = ClosableGate(); gate.Definition.Modes = 3; ActorOn(new Point(0, 2));
        // The interactor's own "free tile" rule blocks 2 -> 0, so cycle the states with the modern Wired arithmetic.
        Func<string, string?> cycle = current => ((int.Parse(current) + 1) % 3).ToString();
        for (var toggle = 0; toggle < 2; toggle++)
            Task.Run(() => GateTransitionService.ToggleState(gate, cycle, GateCloseReason.Wired, persist: false)).Wait();
        Assert.Equal("1", gate.LegacyDataString);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateSequencedAbsoluteToggleBehindAQueuedCloseIsEvaluatedAfterIt()
    {
        var gate = ClosableGate(); gate.Definition.Modes = 3; ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        var seen = new List<string>();
        Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.ToggleState(gate,
            current => { seen.Add(current); return "0"; }, GateCloseReason.Wired, persist: false)).Result);
        Assert.Empty(seen);
        ExecutorTick();
        Assert.Equal(new[] { "2" }, seen); Assert.Equal("0", gate.LegacyDataString);
    }

    [Fact]
    public void GateSequencedWriteArrivingDuringTheBroadcastIsAppendedNotCommitted()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false)).Result);
        GateTransition? duringBroadcast = null; string? stateDuringBroadcast = null; var fired = false;
        _client.BeforeCapture = header =>
        {
            if (header != ServerPacketHeader.ObjectUpdateComposer || fired) return;
            fired = true;
            duringBroadcast = Task.Run(() => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false)).Result;
            stateDuringBroadcast = gate.LegacyDataString;
        };
        DrainOnOwner();
        Assert.Equal(GateTransition.Queued, duringBroadcast); Assert.Equal("0", stateDuringBroadcast);
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateSequencedReentrantWriteFromACallbackIsAppended()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2)); GateTransition? reentrant = null;
        using (RoomOwnerScope.Enter(_room))
        {
            Assert.Equal(GateTransition.Applied, Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false,
                afterClose: changed => reentrant = Gates.Toggle(changed, Flip, GateCloseReason.Wired, persist: false)));
            Assert.Equal(GateTransition.Queued, reentrant); Assert.Equal("0", gate.LegacyDataString);
            Gates.Drain();
        }
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateSequencedNavigationRecordIsPublishedInsideTheMutationBeforeAnyNotification()
    {
        var gate = ClosableGate(width: 2); var (_, navigation) = ActorOn(new Point(0, 2)); var map = _room.GetGameMap();
        var observed = new List<(string Record, bool Placement, bool Nav)>();
        ((Plus.HabboHotel.Items.DataFormat.LegacyDataFormat)gate.ExtraData).DataUpdated += (_, _) =>
            observed.Add((navigation.Inputs.Read(gate.Id)!.State, Monitor.IsEntered(map.PlacementSync), Monitor.IsEntered(gate.NavSync)));
        Assert.Equal(GateTransition.Applied, CloseOnOwner(gate));
        Assert.Equal(new[] { ("0", false, false) }, observed);
    }

    [Fact]
    public void GateSequencedRetainedAutomaticClosesAreDeduplicatedPerGateAndNeverRetriedInTheSameDrain()
    {
        var gate = ClosableGate(); var attempts = 0;
        var service = new GateTransitionService(_room, () => new BlockedOccupancy(() => attempts++));
        Task.Run(() => service.TryClose(gate, GateCloseReason.Automatic, "0", persist: false)).Wait();
        Task.Run(() => service.TryClose(gate, GateCloseReason.Automatic, "0", persist: false)).Wait();
        using (RoomOwnerScope.Enter(_room)) service.Drain();
        Assert.Equal(2, attempts); Assert.Equal(1, service.PendingCount);
        using (RoomOwnerScope.Enter(_room)) service.Drain();
        Assert.Equal(3, attempts); Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public void GateOperationDrainNeverOvertakesAnImmediateOpeningStillPublishing()
    {
        var gate = ClosableGate(state: "0"); ActorOn(new Point(0, 2));
        using var published = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var held = false; var broadcasts = new List<string>();
        _client.BeforeCapture = header =>
        {
            if (header != ServerPacketHeader.ObjectUpdateComposer) return;
            broadcasts.Add(gate.LegacyDataString);
            if (held) return;
            held = true; published.Set(); release.Wait(TimeSpan.FromSeconds(5));
        };
        try
        {
            var opening = Task.Run(() => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false));
            Assert.True(published.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.Apply(gate, "0", GateCloseReason.Wired, persist: false)).Result);
            DrainOnOwner();
            Assert.Equal("1", gate.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
            release.Set();
            Assert.Equal(GateTransition.Applied, opening.Result);
        }
        finally { release.Set(); }
        DrainOnOwner();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
        Assert.Equal(new[] { "1", "0" }, broadcasts);
    }

    [Fact]
    public void GateOperationExecutorGuildOpeningQueuesBehindAPendingCloseAndAppliesItsEffectsAtCommit()
    {
        var gate = ReviewGuildGate(member: true); gate.LegacyDataString = "1";
        var actor = ReviewGateActor(true); var navigation = _room.GetGameMap().Navigation!;
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false)).Result);
        navigation.Executor.Context.GuildGates.Accept(actor, actor.Movement.Profile, navigation.Grid.Tile(1, 1), StepPurpose.Transit);
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, gate.InteractingUser); Assert.Equal(2, Gates.PendingCount);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(7, gate.InteractingUser);
        Assert.Equal(4, gate.UpdateCounter); Assert.True(gate.UpdateNeeded); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateOperationPreparedCloseKeepsItsPositionAheadOfAFollowerSubmittedDuringEvaluation()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        using var evaluating = new ManualResetEventSlim(); using var proceed = new ManualResetEventSlim();
        Func<string, string?> slowClose = _ => { evaluating.Set(); proceed.Wait(TimeSpan.FromSeconds(5)); return "0"; };
        var first = Task.Run(() => Gates.Toggle(gate, slowClose, GateCloseReason.Click, persist: false));
        try
        {
            Assert.True(evaluating.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false)).Result);
        }
        finally { proceed.Set(); }
        Assert.Equal(GateTransition.Queued, first.Result);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateOperationRetainedCloseWaitsWhileAFollowerIsPendingForTheGate()
    {
        var gate = ClosableGate(); var (actor, navigation) = ActorOn(new Point(0, 2));
        Assert.True(navigation.Executor.Claims.TryClaim(actor, navigation.Grid.Tile(1, 1), ClaimKind.Shared, TargetOccupancy.None));
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(gate, GateCloseReason.Automatic, "0", persist: false)).Result);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(1, Gates.PendingCount);
        navigation.Executor.Claims.Release(actor);
        GateTransition? follower = null;
        Assert.Equal(GateTransition.Queued, Task.Run(() => Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false,
            afterClose: _ => follower = GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false))).Result);
        DrainOnOwner();
        Assert.Equal(GateTransition.Queued, follower);
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(2, Gates.PendingCount);
    }

    [Fact]
    public void GateLaneLegacySnapshotRestoreBehindAQueuedCloseIsEvaluatedAtExecution()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        ClickFromPacketThread(gate);
        var box = new MatchPositionBox(_room, Furni(22, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition))
        { StringData = "1;0;0", ItemsData = $"{gate.Id}:1,1,0,0,1" };
        box.SetItems.TryAdd(gate.Id, gate);
        using (RoomOwnerScope.Enter(_room)) Assert.True(box.Execute());
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(2, Gates.PendingCount);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateLaneLegacySnapshotRestoreOnAnIdleGateStillSkipsEqualAndAppliesDifferent()
    {
        var gate = ClosableGate(state: "0"); ActorOn(new Point(0, 2)); var updates = 0;
        _client.BeforeCapture = header => { if (header == ServerPacketHeader.ObjectUpdateComposer) updates++; };
        foreach (var restored in new[] { "0", "1" })
        {
            var box = new MatchPositionBox(_room, Furni(22, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition))
            { StringData = "1;0;0", ItemsData = $"{gate.Id}:1,1,0,0,{restored}" };
            box.SetItems.TryAdd(gate.Id, gate);
            using (RoomOwnerScope.Enter(_room)) box.Execute();
            Assert.Equal(restored, gate.LegacyDataString);
            if (restored == "0") Assert.Equal(0, updates);
        }
        Assert.Equal(1, updates);
    }

    [Fact]
    public void GateLaneModernSnapshotRestoreBehindAQueuedCloseIsEvaluatedAtExecution()
    {
        var gate = ClosableGate(); ActorOn(new Point(0, 2));
        var box = Furni(41, InteractionType.WiredEffect, WiredBoxType.None);
        var action = new Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction(_room, box,
            Plus.HabboHotel.Items.Wired.Configuration.WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_match_to_sshot"),
            new(), _ => { }, (_, _, _) => { }, new(), TestLogging.Logger, TimeProvider.System, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestWiredDefinitions.Unused, TestItemRuntime.Travel);
        var proposed = new Plus.HabboHotel.Items.Wired.Configuration.WiredConfiguration
        {
            IntParams = [1, 0, 0, 0, 100], SelectedItems = [gate.Id],
            Snapshots = [new(gate.Id, 0, gate.GetX, gate.GetY, gate.GetZ, gate.Rotation, "1")]
        };
        Assert.True(action.TryValidateConfiguration(proposed, out var config, out var error), error);
        action.ApplyConfiguration(config);
        var items = _room.GetRoomItemHandler().GetFloor.ToArray(); var users = _room.GetRoomUserManager().GetUserList().ToArray();
        var context = new Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext(_room,
            new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Use), new(() => items, () => users), new NoWiredOperations());
        ClickFromPacketThread(gate);
        _room.RunFastPass(() => action.Execute(context));
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(2, Gates.PendingCount);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }
}
