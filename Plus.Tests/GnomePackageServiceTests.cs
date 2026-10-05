using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Database;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class GnomePackageServiceTests
{
    [Fact]
    public async Task HandlerConsumesIdAndNameAndDelegates()
    {
        var service = new RecordingService();
        var packet = HabbiconTestSupport.Incoming(7, "Pixel");
        await new CheckGnomeNameEvent(service).Parse(null!, null!, packet);
        Assert.Equal((7u, "Pixel"), service.Request);
        Assert.False(packet.HasDataRemaining());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("temporary")]
    [InlineData("type")]
    [InlineData("room")]
    [InlineData("current")]
    public void InvalidPackageContextDoesNotReachStore(string denial)
    {
        var world = World(denial == "temporary");
        switch (denial)
        {
            case "owner": world.Item.OwnerId = 99; break;
            case "temporary": break;
            case "type": world.Item.Definition.InteractionType = InteractionType.None; break;
            case "room": world.Item.RoomId = 99; break;
            case "current": world.Client.GetHabbo().CurrentRoom = null; break;
        }
        world.Service.Open(world.Room, world.Client, 7, "Pixel");
        Assert.Empty(world.Store.Requests);
        Assert.Empty(world.Packets);
        Assert.Same(world.Item, world.Room.GetRoomItemHandler().GetItem(7));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name!")]
    [InlineData("abcdefghijklmnopq")]
    public void InvalidNameRepliesWithoutConsumingPackage(string name)
    {
        var world = World();
        world.Service.Open(world.Room, world.Client, 7, name);
        Assert.Empty(world.Store.Requests);
        Assert.Single(world.Packets);
        Assert.Same(world.Item, world.Room.GetRoomItemHandler().GetItem(7));
    }

    [Fact]
    public void FailedTransactionLeavesPackageAndRoomPetsUnchanged()
    {
        var world = World();
        world.Store.Fail = true;
        world.Service.Open(world.Room, world.Client, 7, "Pixel");
        Assert.Single(world.Store.Requests);
        Assert.Same(world.Item, world.Room.GetRoomItemHandler().GetItem(7));
        Assert.False(world.Room.GetRoomUserManager().TryGetPet(12, out _));
        Assert.Single(world.Packets); // Existing failure notification only.
    }

    [Fact]
    public void SuccessPersistsBeforePublicationAndDoubleOpenCreatesOnePet()
    {
        var world = World();
        world.Store.Before = request =>
        {
            Assert.Same(world.Item, world.Room.GetRoomItemHandler().GetItem(7));
            Assert.False(world.Room.GetRoomUserManager().TryGetPet(12, out _));
            Assert.Empty(world.Packets);
            Assert.Equal((7u, 100u, 1, "owner", 42u, 1, 1, 0d, "Pixel"),
                (request.ItemId, request.BaseItem, request.OwnerId, request.OwnerName, request.RoomId,
                    request.X, request.Y, request.Z, request.Name));
            Assert.Equal(world.Clock.Now, request.CreatedAt);
        };
        world.Service.Open(world.Room, world.Client, 7, "Pixel");
        world.Service.Open(world.Room, world.Client, 7, "Again");
        Assert.Single(world.Store.Requests);
        Assert.Null(world.Room.GetRoomItemHandler().GetItem(7));
        Assert.True(world.Room.GetRoomUserManager().TryGetPet(12, out var actor));
        Assert.Equal(("owner", "Pixel", 26, 42u),
            (actor.PetData.OwnerName, actor.PetData.Name, actor.PetData.Type, actor.PetData.RoomId));
        Assert.Equal(1, world.Clock.Reads);
        Assert.NotEmpty(world.Packets);
    }

    [PetPlacementDatabaseFact]
    public void StoreCommitsPetAndPackageTogetherAndRollsBackExactMismatchOrSecondWriteFailure()
    {
        using var root = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        root.Open();
        var schema = "gnome_" + Guid.NewGuid().ToString("N");
        root.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var cs = new MySqlConnectionStringBuilder(root.ConnectionString)
            {
                Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true
            }.ConnectionString;
            using var connection = new MySqlConnection(cs);
            connection.Open();
            connection.Execute("""
                CREATE TABLE bots (id INT AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL,
                    name VARCHAR(50) NOT NULL, motto VARCHAR(50) NOT NULL, look VARCHAR(100) NOT NULL,
                    x INT NOT NULL, y INT NOT NULL, z DOUBLE NOT NULL, ai_type VARCHAR(20) NOT NULL) ENGINE=InnoDB;
                CREATE TABLE bots_petdata (id INT PRIMARY KEY, type INT NOT NULL, race VARCHAR(20) NOT NULL, color VARCHAR(20) NOT NULL,
                    experience INT NOT NULL, energy INT NOT NULL, nutrition INT NOT NULL, respect INT NOT NULL,
                    createstamp DATETIME(6) NULL, have_saddle INT NOT NULL, anyone_ride INT NOT NULL,
                    hairdye INT NOT NULL, pethair INT NOT NULL, gnome_clothing VARCHAR(100) NOT NULL) ENGINE=InnoDB;
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL,
                    base_item INT UNSIGNED NOT NULL) ENGINE=InnoDB;
                INSERT INTO items VALUES (7, 1, 42, 100);
                """);
            var store = new GnomePackageStore(new ProbeDatabase(cs), NullLogger<GnomePackageStore>.Instance);
            var instant = DateTimeOffset.Parse("2040-01-01T00:00:00.123456Z");
            var request = new GnomePackageRequest(7, 100, 1, "prepared owner", 42, 1, 1, 0.5, "Pixel", "hat", instant);
            foreach (var invalid in new[] { request with { OwnerId = 99 }, request with { RoomId = 99 }, request with { BaseItem = 101 } })
            {
                Assert.Null(store.Open(invalid));
                Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bots"));
                Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bots_petdata"));
                Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            }
            connection.Execute("CREATE TRIGGER fail_pet BEFORE INSERT ON bots_petdata FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced pet failure'");
            Assert.Null(store.Open(request));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bots"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            connection.Execute("DROP TRIGGER fail_pet");
            var pet = Assert.IsType<Pet>(store.Open(request));
            Assert.Equal(("prepared owner", 42u, instant, 0.5), (pet.OwnerName, pet.RoomId, pet.CreatedAt, pet.Z));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bots"));
            Assert.Equal(instant, connection.QuerySingle<DateTimeOffset>("SELECT createstamp FROM bots_petdata"));
            Assert.Empty(connection.Query<int>("SELECT id FROM items"));
            Assert.Null(store.Open(request));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bots"));
        }
        finally { root.Execute($"DROP DATABASE `{schema}`"); }
    }

    private static Fixture World(bool temporary = false)
    {
        var model = new RoomModel("gnome-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, false);
        var room = new Room(new RoomData { Id = 42, Model = model }, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
        Set(room, "_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        Set(room, "_gamemap", new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        Set(room, "_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty));
        Set(room, "_userSnapshots", CatalogSnapshotTestSupport.Proxy<IRoomUserSnapshotService>((_, _) => null));
        var item = new Item { Id = 7, BaseItem = 100, OwnerId = 1, RoomId = 42, IsTemporary = temporary,
            Definition = new() { Id = 100, Type = ItemType.Floor, InteractionType = InteractionType.GnomeBox },
            ExtraData = new LegacyDataFormat { Data = "" } };
        item.SetState(1, 1, 0, new());
        Set(item, "_room", room);
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(room.GetRoomItemHandler())!;
        floor.AddOrUpdate(7, item, (_, _) => item);
        room.GetGameMap().GenerateMaps();
        var (client, packets) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "owner", CurrentRoom = room,
            Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) } });
        var store = new RecordingStore();
        var clock = new CountingClock();
        var definitions = CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "get_Items" ? new Dictionary<uint, ItemDefinition>() : null);
        var factory = CatalogSnapshotTestSupport.Proxy<IItemFactory>((_, _) => throw new InvalidOperationException("No food definition supplied."));
        return new(room, item, client, packets, store, clock, new(store, definitions, factory, clock));
    }

    private static void Set(object instance, string field, object value) => instance.GetType()
        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);
    private sealed record Fixture(Room Room, Item Item, GameClient Client, List<(uint Header, byte[] Payload)> Packets,
        RecordingStore Store, CountingClock Clock, GnomePackageService Service);
    private sealed class RecordingService : IGnomePackageService
    {
        public (uint, string)? Request;
        public void Open(Room room, GameClient session, uint itemId, string petName) => Request = (itemId, petName);
    }
    private sealed class RecordingStore : IGnomePackageStore
    {
        public List<GnomePackageRequest> Requests { get; } = [];
        public Action<GnomePackageRequest>? Before;
        public bool Fail;
        public Pet? Open(GnomePackageRequest request)
        {
            Before?.Invoke(request);
            Requests.Add(request);
            return Fail ? null : new Pet(12, request.OwnerId, request.RoomId, request.Name, 26, "30", "ffffff", 0,
                100, 100, 0, request.CreatedAt, request.X, request.Y, request.Z, 0, 0, 0, -1, request.Clothing, request.OwnerName);
        }
    }
    private sealed class CountingClock : TimeProvider
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2040-01-01T00:00:00Z");
        public int Reads;
        public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
    }
    private sealed class ProbeDatabase(string cs) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(cs);
    }
}
