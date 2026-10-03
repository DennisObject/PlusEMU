using System.Drawing;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// §16.3: every writer that closes a gate goes through one owner-task operation.
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
        using (RoomOwnerScope.Enter(_room)) new InteractorGenericSwitch().OnTrigger(_client, gate, 0, true);
        Assert.Equal(expected, gate.LegacyDataString);
    }

    [Fact]
    public void GateGenericSwitchStillTogglesPlainFurnitureThroughOccupants()
    {
        UseGameService("get_QuestManager", Proxy<IQuestManager>((_, _) => null));
        var lamp = ClosableGate(InteractionType.None);
        ActorOn(lamp.Coordinate);
        using (RoomOwnerScope.Enter(_room)) new InteractorGenericSwitch().OnTrigger(_client, lamp, 0, true);
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
    public void GateLegacyEngineUsesLegacyOccupancyOnEveryFootprintTile()
    {
        var gate = ClosableGate(width: 2);
        Assert.Null(_room.GetGameMap().Navigation);
        var user = Viewer(NonAnchor(gate).X, NonAnchor(gate).Y);
        _room.GetGameMap().AddUserToMap(user, NonAnchor(gate));
        Assert.Equal(GateTransition.Refused, CloseOnOwner(gate)); Assert.Equal("1", gate.LegacyDataString);
        _room.GetGameMap().RemoveUserFromMap(user, NonAnchor(gate));
        Assert.Equal(GateTransition.Applied, CloseOnOwner(gate)); Assert.Equal("0", gate.LegacyDataString);
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
}
