using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Language;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.Interactor;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void ItemInteractorRequiresAdmissionAndCreatesANewInteractorPerAccess()
    {
        var item = Furni(89, InteractionType.None, WiredBoxType.None);
        var error = Assert.Throws<InvalidOperationException>(() => item.Interactor);
        Assert.Contains("attached to a room", error.Message);

        Assert.True(Handler(new()).SetFloorItem(_client, item, 1, 1, 0, true, false, false));

        Assert.NotSame(item.Interactor, item.Interactor);
        Assert.Same(_room, item.GetRoom());
    }

    [Fact]
    public void AdmissionRejectsForeignOwnershipAndWrongRoomDetachWithoutChangingGeometry()
    {
        var item = Furni(88, InteractionType.None, WiredBoxType.None);
        var handler = Handler(new());
        Assert.True(handler.SetFloorItem(_client, item, 1, 1, 0, true, false, false));
        var foreign = (Room)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Room));
        foreign.Id = _room.RoomId;

        Assert.Throws<InvalidOperationException>(() => item.Detach(foreign));
        Assert.Throws<InvalidOperationException>(() => item.Attach(foreign, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        Assert.Same(_room, item.GetRoom());
        Assert.Equal((1, 1, 0d), (item.GetX, item.GetY, item.GetZ));
        Assert.Contains(item, _room.GetGameMap().GetCoordinatedItems(new(1, 1)));
    }

    [Fact]
    public void DuplicateAdmissionDoesNotRebindOrMoveEitherItem()
    {
        var original = Furni(87, InteractionType.None, WiredBoxType.None);
        var duplicate = Furni(87, InteractionType.None, WiredBoxType.None);
        var handler = Handler(new());
        Assert.True(handler.SetFloorItem(_client, original, 1, 1, 0, true, false, false));

        Assert.True(handler.SetFloorItem(_client, duplicate, 2, 2, 0, true, false, false));

        Assert.Same(_room, original.GetRoom());
        Assert.Null(duplicate.GetRoom());
        Assert.Equal((1, 1), (original.GetX, original.GetY));
        Assert.Equal((0, 0), (duplicate.GetX, duplicate.GetY));
        Assert.DoesNotContain(duplicate, _room.GetGameMap().GetCoordinatedItems(new(2, 2)));
    }

    [Fact]
    public void HopperPersistenceFailureLeavesAdmissionGeometryAndPublicationUnchanged()
    {
        var travel = new FailingTravelStore();
        var interactors = new ItemInteractorFactory(travel, TestItemRuntime.Profiles,
            TestItemRuntime.Quests, TestItemRuntime.Rewards, TestItemRuntime.Achievements);
        var handler = new RoomItemHandling(_room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, interactors, travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room, handler);
        var item = Furni(86, InteractionType.Hopper, WiredBoxType.None);
        item.InteractingUser = 123;
        var oldGeometry = (item.GetX, item.GetY, item.GetZ, item.Rotation, item.RoomId);
        var sent = CaptureTransport(_client);

        Assert.Throws<InvalidOperationException>(() => handler.SetFloorItem(_client, item, 1, 1, 0, true, false, true));

        Assert.Equal(0, handler.HopperCount);
        Assert.Equal(123, item.InteractingUser);
        Assert.Null(item.GetRoom());
        Assert.Empty(handler.GetFloor);
        Assert.DoesNotContain(item, _room.GetGameMap().GetCoordinatedItems(new(1, 1)));
        Assert.Equal(oldGeometry, (item.GetX, item.GetY, item.GetZ, item.Rotation, item.RoomId));
        Assert.Empty(MovedItems());
        Assert.Empty(sent);
    }

    [Fact]
    public void InvalidLoadedFloorItemClearsPersistenceBeforeReturningItToOnlineOwner()
    {
        var events = new List<string>();
        var store = new DependencyRoomItemStore
        {
            Clear = id =>
            {
                events.Add($"clear:{id}");
                Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(id));
            }
        };
        var clients = new TestGameClientManager(id =>
        {
            Assert.Equal(new[] { "clear:90" }, events);
            events.Add($"lookup:{id}");
            return _client;
        });
        var handler = Handler(store, clients);
        var item = InvalidFloorItem(90);
        var sent = CaptureTransport(_client, () =>
        {
            Assert.Equal(1, store.ClearCount);
            Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(90));
        });

        WithUnavailableItemGlobals(() => handler.LoadFurniture([item]));

        Assert.Equal(new[] { "clear:90", "lookup:7" }, events);
        Assert.Equal(1, store.ClearCount);
        var returned = Assert.IsType<InventoryItem>(_client.GetHabbo().Inventory.Furniture.GetItem(90));
        Assert.Equal(item.Id, returned.Id);
        Assert.Equal(ServerPacketHeader.FurniListUpdateComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public void InvalidLoadedFloorItemClearsPersistenceWithoutPublishingForOfflineOwner()
    {
        var store = new DependencyRoomItemStore();
        var handler = Handler(store, new TestGameClientManager(id =>
        {
            Assert.Equal(7, id);
            Assert.Equal(1, store.ClearCount);
            return null;
        }));
        var sent = CaptureTransport(_client);

        WithUnavailableItemGlobals(() => handler.LoadFurniture([InvalidFloorItem(91)]));

        Assert.Equal(1, store.ClearCount);
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(91));
        Assert.Empty(sent);
    }

    [Fact]
    public void DuplicateFloorPlacementUsesInjectedNoticeWithoutRepersistingOrPublishing()
    {
        var store = new DependencyRoomItemStore();
        var handler = Handler(store);
        var item = Furni(92, InteractionType.None, WiredBoxType.None);
        Assert.True(handler.SetFloorItem(_client, item, 1, 1, 0, true, false, false));
        var writes = store.FloorPlacements;
        var sent = CaptureTransport(_client);

        var result = false;
        WithUnavailableItemGlobals(() => result = handler.SetFloorItem(_client, item, 2, 2, 0, true, false, true));

        Assert.True(result);
        Assert.Equal(writes, store.FloorPlacements);
        Assert.Single(handler.GetFloor);
        Assert.Equal((1, 1), (item.GetX, item.GetY));
        Assert.Contains(item, _room.GetGameMap().GetCoordinatedItems(new(1, 1)));
        Assert.DoesNotContain(item, _room.GetGameMap().GetCoordinatedItems(new(2, 2)));
        AssertNotice(Assert.Single(sent));
    }

    [Fact]
    public void WallPlacementDistinguishesFloorCollisionFromExistingWallDuplicate()
    {
        var store = new DependencyRoomItemStore();
        var handler = Handler(store);
        var floor = Furni(93, InteractionType.None, WiredBoxType.None);
        Assert.True(handler.SetFloorItem(_client, floor, 1, 1, 0, true, false, false));
        var wallCollision = Furni(93, InteractionType.None, WiredBoxType.None, ItemType.Wall);
        var sent = CaptureTransport(_client);

        var collisionResult = false;
        WithUnavailableItemGlobals(() => collisionResult = handler.SetWallItem(_client, wallCollision));

        Assert.True(collisionResult);
        Assert.Equal(0, store.WallPlacements);
        Assert.Empty(handler.GetWall);
        AssertNotice(Assert.Single(sent));

        var wall = Furni(94, InteractionType.None, WiredBoxType.None, ItemType.Wall);
        Assert.True(handler.SetWallItem(_client, wall));
        var writes = store.WallPlacements;
        sent.Clear();

        var duplicateResult = true;
        WithUnavailableItemGlobals(() => duplicateResult = handler.SetWallItem(_client, wall));

        Assert.False(duplicateResult);
        Assert.Equal(writes, store.WallPlacements);
        Assert.Empty(sent);
    }

    private RoomItemHandling Handler(DependencyRoomItemStore store, IGameClientManager? clients = null,
        IItemTravelStore? travel = null)
    {
        _client.GetHabbo().Inventory ??= new InventoryComponent
        {
            Furniture = new FurnitureInventoryComponent([], [])
        };
        travel ??= TestItemRuntime.Travel;
        var interactors = ReferenceEquals(travel, TestItemRuntime.Travel) ? TestItemRuntime.Interactors
            : new ItemInteractorFactory(travel, TestItemRuntime.Profiles, TestItemRuntime.Quests,
                TestItemRuntime.Rewards, TestItemRuntime.Achievements);
        var handler = new RoomItemHandling(_room, store, TestRoomItemMetadataStore.Instance,
            clients ?? TestGameClientManager.Empty,
            new TestLanguageManager(new Dictionary<string, string>
            {
                ["room.item.already_placed"] = "localized duplicate"
            }), interactors, travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_room, handler);
        return handler;
    }

    private Item InvalidFloorItem(uint id)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None);
        item.UserId = 7;
        item.SetState(99, 99, 0, new Dictionary<int, ThreeDCoord>());
        return item;
    }

    private void WithUnavailableItemGlobals(Action action)
    {
        var languageField = typeof(PlusEnvironment).GetField("_languageManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousGame = _gameField.GetValue(null);
        var previousLanguage = languageField.GetValue(null);
        try
        {
            _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException($"global game:{method}")));
            languageField.SetValue(null, Proxy<ILanguageManager>((method, _) => throw new InvalidOperationException($"global language:{method}")));
            action();
        }
        finally
        {
            _gameField.SetValue(null, previousGame);
            languageField.SetValue(null, previousLanguage);
        }
    }

    private static List<CapturedPacket> CaptureTransport(GameClient client, Action? beforeCapture = null)
    {
        var sent = new List<CapturedPacket>();
        client.SendCallback = args =>
        {
            beforeCapture?.Invoke();
            var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
            sent.Add(new((uint)FlashGameClient.DecodeInt16(bytes.AsMemory(4, 2)), bytes));
            return false;
        };
        return sent;
    }

    private static void AssertNotice(CapturedPacket packet)
    {
        Assert.Equal(ServerPacketHeader.BroadcastMessageAlertComposer, packet.Header);
        var body = new FlashIncomingPacket { Buffer = packet.Bytes[6..] };
        Assert.Equal("localized duplicate", body.ReadString());
        Assert.Equal(string.Empty, body.ReadString());
        Assert.False(body.HasDataRemaining());
    }

    private sealed record CapturedPacket(uint Header, byte[] Bytes);

    private sealed class DependencyRoomItemStore : IRoomItemStore
    {
        public Action<uint>? Clear { get; init; }
        public int ClearCount { get; private set; }
        public int FloorPlacements { get; private set; }
        public int WallPlacements { get; private set; }
        public void AssignOwner(uint itemId, int userId) { }
        public void ClearRoom(uint itemId) { ClearCount++; Clear?.Invoke(itemId); }
        public void SaveWallPosition(uint itemId, string wallPosition) { }
        public void SaveMoved(IReadOnlyList<RoomItemSave> items) { }
        public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) => FloorPlacements++;
        public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) => WallPlacements++;
    }

    [Fact]
    public void ReloadDetachesTemporaryFloorItemExactlyOnce()
    {
        var handler = Handler(new());
        var definition = Furni(85, InteractionType.None, WiredBoxType.None).Definition;
        var temporary = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(definition, 7, 1, 1, 0));

        handler.LoadFurniture([]);

        Assert.Null(temporary.GetRoom());
        Assert.Empty(handler.GetFloor);
    }

    [Fact]
    public void RepeatingExactPublicAdmissionKeepsTheRegisteredItemBound()
    {
        var item = Furni(84, InteractionType.None, WiredBoxType.None);
        var handler = Handler(new());
        Assert.True(handler.SetFloorItem(_client, item, 1, 1, 0, true, false, false));

        Assert.True(handler.SetFloorItem(_client, item, 2, 2, 0, true, false, false));

        Assert.Same(_room, item.GetRoom());
        Assert.Same(item, Assert.Single(handler.GetFloor));
        Assert.Equal((1, 1), (item.GetX, item.GetY));
    }

    private sealed class FailingTravelStore : IItemTravelStore
    {
        public uint FindOtherHopperRoom(uint roomId) => 0;
        public uint FindHopper(uint roomId) => 0;
        public uint FindLinkedTeleporter(uint itemId) => 0;
        public uint FindItemRoom(uint itemId) => 0;
        public void RegisterHopper(uint itemId, uint roomId) => throw new InvalidOperationException("hopper write failed");
        public void RemoveHopper(uint itemId, uint roomId) => throw new InvalidOperationException("hopper delete failed");
    }
}
