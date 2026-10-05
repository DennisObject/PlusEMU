using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class GiftOpeningServiceTests
{
    [Fact]
    public async Task ValidGiftPersistsBeforePublicationAndRepeatedOpenPaysOnce()
    {
        var (room, client, sent, gift) = Context();
        var definition = Definition(200, InteractionType.None);
        var store = new Store(() =>
        {
            Assert.Same(gift, room.GetRoomItemHandler().GetItem(gift.Id));
            Assert.Equal(100, gift.BaseItem);
            Assert.Empty(sent);
        });
        var service = Service(store, definition);

        await service.OpenAsync(client, gift.Id);
        await service.OpenAsync(client, gift.Id);

        Assert.Equal(1, store.Opens);
        Assert.Equal(100, gift.BaseItem);
        Assert.NotSame(definition, gift.Definition);
        var delivered = Assert.IsType<InventoryItem>(client.GetHabbo().Inventory.Furniture.GetItem(gift.Id));
        Assert.Same(definition, delivered.Definition);
        Assert.Equal("blue", delivered.ExtraData.Serialize());
        Assert.NotEmpty(sent);
    }

    [Fact]
    public async Task MalformedGiftIsCleanedOnlyAfterTransactionalDelete()
    {
        var (room, client, sent, gift) = Context("broken");
        var store = new Store(() =>
        {
            Assert.Same(gift, room.GetRoomItemHandler().GetItem(gift.Id));
            Assert.Empty(sent);
        });

        await Service(store, Definition(200, InteractionType.None)).OpenAsync(client, gift.Id);

        Assert.Equal(1, store.InvalidDeletes);
        Assert.Null(room.GetRoomItemHandler().GetItem(gift.Id));
        Assert.NotEmpty(sent);
    }

    [Fact]
    public async Task NonOwnerAndTemporaryGiftsAreDeniedWithoutStoreCalls()
    {
        var (room, client, sent, gift) = Context();
        var store = new Store();
        gift.OwnerId = 99;
        await Service(store, Definition(200, InteractionType.None)).OpenAsync(client, gift.Id);
        var (temporaryRoom, temporaryClient, _, temporaryGift) = Context(temporary: true);
        await Service(store, Definition(200, InteractionType.None)).OpenAsync(temporaryClient, temporaryGift.Id);

        Assert.Equal(0, store.Opens);
        Assert.Equal(0, store.InvalidDeletes);
        Assert.Same(gift, room.GetRoomItemHandler().GetItem(gift.Id));
        Assert.Same(temporaryGift, temporaryRoom.GetRoomItemHandler().GetItem(temporaryGift.Id));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task PersistenceFailureLeavesGiftAndPacketsUnchanged()
    {
        var (room, client, sent, gift) = Context();
        var originalDefinition = gift.Definition;
        var store = new Store { FailOpen = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, Definition(200, InteractionType.None)).OpenAsync(client, gift.Id));

        Assert.Same(gift, room.GetRoomItemHandler().GetItem(gift.Id));
        Assert.Same(originalDefinition, gift.Definition);
        Assert.Equal(100, gift.BaseItem);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task UnrepresentableReplacementDefinitionDoesNotConsumeGift()
    {
        var (room, client, sent, gift) = Context();
        var store = new Store { Content = new(uint.MaxValue, "blue") };
        var definition = Definition(200, InteractionType.None);
        definition.Id = uint.MaxValue;

        await Service(store, definition).OpenAsync(client, gift.Id);

        Assert.Equal(0, store.Opens);
        Assert.Same(gift, room.GetRoomItemHandler().GetItem(gift.Id));
        Assert.NotEmpty(sent);
    }

    [Fact]
    public async Task FailedFloorPlacementDeliversPreparedInventoryFallback()
    {
        var (room, client, sent, gift) = Context();
        var store = new Store();
        var definition = Definition(200, InteractionType.None);
        definition.Type = ItemType.Floor;
        gift.GetX = 99;
        gift.GetY = 99;

        await Service(store, definition).OpenAsync(client, gift.Id);

        var delivered = Assert.IsType<InventoryItem>(client.GetHabbo().Inventory.Furniture.GetItem(gift.Id));
        Assert.Same(definition, delivered.Definition);
        Assert.Null(room.GetRoomItemHandler().GetItem(gift.Id));
        Assert.NotEmpty(sent);
    }

    [Fact]
    public async Task PlacementPersistenceFailureRemovesAdmittedReplacementBeforeInventoryFallback()
    {
        var placement = new FailingPlacement();
        var (room, client, _, gift) = Context(itemStore: placement);
        gift.GetX = 1;
        gift.GetY = 1;
        var definition = Definition(200, InteractionType.None);
        definition.Type = ItemType.Floor;
        definition.Width = 1;
        definition.Length = 1;

        await Service(new Store(), definition).OpenAsync(client, gift.Id);

        Assert.Equal(1, placement.Attempts);
        Assert.Null(room.GetRoomItemHandler().GetItem(gift.Id));
        var inventory = Assert.IsType<InventoryItem>(client.GetHabbo().Inventory.Furniture.GetItem(gift.Id));
        Assert.Same(definition, inventory.Definition);
        Assert.Empty(room.GetGameMap().GetCoordinatedItems(new(1,1)));
    }

    private sealed class FailingPlacement : IRoomItemStore
    {
        public int Attempts { get; private set; }
        public void AssignOwner(uint itemId, int userId) { }
        public void ClearRoom(uint itemId) { }
        public void SaveWallPosition(uint itemId, string wallPosition) { }
        public void SaveMoved(IReadOnlyList<RoomItemSave> items) { }
        public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation)
        {
            Attempts++;
            throw new InvalidOperationException("Forced placement persistence failure.");
        }
        public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) => throw new NotSupportedException();
    }

    private static GiftOpeningService Service(Store store, ItemDefinition definition) =>
        new(store, new ItemCatalog(definition), new Cache(), NullLogger<GiftOpeningService>.Instance);

    private static (Room Room, Plus.HabboHotel.GameClients.GameClient Client, List<(uint Header, byte[] Payload)> Sent, Item Gift) Context(string data = "a\u0005b\u00052", bool temporary = false, IRoomItemStore? itemStore = null)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, itemStore ?? TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room,
            new Gamemap(room, new RoomModel("gift-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        var gift = new Item { Id = 7, RoomId = 9, OwnerId = 1, BaseItem = 100, IsTemporary = temporary, Definition = Definition(100, InteractionType.Gift), ExtraData = new LegacyDataFormat { Data = data } };
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(gift, room);
        var walls = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_wallItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        walls[gift.Id] = gift;
        var inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) };
        var habbo = new Habbo { Id = 1, Username = "owner", CurrentRoom = room, Inventory = inventory };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        return (room, client, sent, gift);
    }

    private static ItemDefinition Definition(int id, InteractionType type) => new() { Id = (uint)id, Type = ItemType.Wall, InteractionType = type, SpriteId = id, ItemName = $"item-{id}" };

    private sealed class Store(Action? before = null) : IGiftStore
    {
        public bool FailOpen; public int Opens; public int InvalidDeletes;
        public GiftContent Content { get; init; } = new(200, "blue");
        public GiftContent? Find(uint itemId) => Content;
        public void Open(uint itemId, int ownerId, uint roomId, uint presentBaseId, GiftContent content) { before?.Invoke(); if (FailOpen) throw new InvalidOperationException("forced"); Opens++; }
        public void DeleteInvalid(uint itemId, int ownerId, uint roomId) { before?.Invoke(); InvalidDeletes++; }
    }
    private sealed class ItemCatalog(ItemDefinition definition) : IItemDataManager
    {
        public Dictionary<int, uint> Gifts { get; } = [];
        public Dictionary<uint, ItemDefinition> Items { get; } = new() { [(uint)definition.Id] = definition };
        public void Init() { }
        public ItemDefinition GetItemByName(string name) => definition;
    }
    private sealed class Cache : ICacheManager
    {
        public CachedUser? GenerateUser(int id) => new() { Id = id, Username = "buyer" };
        public bool ContainsUser(int id) => true;
        public bool TryRemoveUser(int id, out CachedUser cachedUser) { cachedUser = null!; return false; }
        public bool TryGetUser(int id, out CachedUser cachedUser) { cachedUser = GenerateUser(id)!; return true; }
        public ICollection<CachedUser> GetUserCache() => [];
        public void Init() { }
    }
}
