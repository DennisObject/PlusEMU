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
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
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
            events.Add($"lookup:{id}");
            Assert.Equal(new[] { "clear:90" }, events);
            return _client;
        });
        var handler = Handler(store, clients);
        var item = InvalidFloorItem(90);
        var sent = CaptureTransport(_client);

        WithUnavailableItemGlobals(() => handler.LoadFurniture([item]));

        Assert.Equal(new[] { "clear:90", "lookup:7" }, events);
        Assert.Equal(1, store.ClearCount);
        var returned = Assert.IsType<InventoryItem>(_client.GetHabbo().Inventory.Furniture.GetItem(90));
        Assert.Equal(item.Id, returned.Id);
        Assert.Equal(ServerPacketHeader.FurniListUpdateComposer, Header(Assert.Single(sent)));
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

    private RoomItemHandling Handler(DependencyRoomItemStore store, IGameClientManager? clients = null)
    {
        _client.GetHabbo().Inventory ??= new InventoryComponent
        {
            Furniture = new FurnitureInventoryComponent([], [])
        };
        var handler = new RoomItemHandling(_room, store, TestRoomItemMetadataStore.Instance,
            clients ?? TestGameClientManager.Empty,
            new TestLanguageManager(new Dictionary<string, string>
            {
                ["room.item.already_placed"] = "localized duplicate"
            }));
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

    private static List<byte[]> CaptureTransport(GameClient client)
    {
        var sent = new List<byte[]>();
        client.SendCallback = args =>
        {
            sent.Add(args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray());
            return false;
        };
        return sent;
    }

    private static uint Header(byte[] packet) => (uint)FlashGameClient.DecodeInt16(packet.AsMemory(4));

    private static void AssertNotice(byte[] packet)
    {
        Assert.Equal(ServerPacketHeader.BroadcastMessageAlertComposer, Header(packet));
        var body = new FlashIncomingPacket { Buffer = packet[6..] };
        Assert.Equal("localized duplicate", body.ReadString());
        Assert.False(body.HasDataRemaining());
    }

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
}
