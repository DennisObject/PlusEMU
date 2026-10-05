using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class FurnitureInventoryPersistenceTests
{
    [Fact]
    public void ClearPublishesOnlyAfterPersistence()
    {
        var context = Context();
        var store = new RecordingStore(() =>
        {
            context.Events.Add("store");
            Assert.NotNull(context.Habbo.Inventory.Furniture.GetItem(7));
            Assert.Empty(context.Sent);
        });

        Assert.True(new InventoryClearService(store, context.Gate).TryClear(context.Client, context.Room));

        Assert.Equal(new[] { "gate-enter", "store", "gate-exit" }, context.Events);
        Assert.Null(context.Habbo.Inventory.Furniture.GetItem(7));
        Assert.Equal(2, context.Sent.Count);
    }

    [Fact]
    public void StoreFailureLeavesInventoryAndPacketsUntouched()
    {
        var context = Context();
        var store = new RecordingStore(() => context.Events.Add("store")) { Failure = new InvalidOperationException("forced") };

        Assert.Throws<InvalidOperationException>(() => new InventoryClearService(store, context.Gate).TryClear(context.Client, context.Room));

        Assert.NotNull(context.Habbo.Inventory.Furniture.GetItem(7));
        Assert.Empty(context.Sent);
        Assert.Equal(new[] { "gate-enter", "store", "gate-exit" }, context.Events);
    }

    [Fact]
    public void ActiveTradeIsRefusedWithoutPersistenceOrPublication()
    {
        var context = Context();
        context.RoomUser.IsTrading = true;
        var store = new RecordingStore();

        Assert.False(new InventoryClearService(store, context.Gate).TryClear(context.Client, context.Room));

        Assert.Equal(0, store.Calls);
        Assert.NotNull(context.Habbo.Inventory.Furniture.GetItem(7));
        Assert.Empty(context.Sent);
    }

    [Fact]
    public void StaleOrClosedSessionIsRefusedWithoutPersistence()
    {
        var stale = Context();
        stale.Habbo.Client = null!;
        var staleStore = new RecordingStore();
        Assert.False(new InventoryClearService(staleStore, stale.Gate).TryClear(stale.Client, stale.Room));
        Assert.Equal(0, staleStore.Calls);

        var closed = Context();
        typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(closed.Habbo, true);
        var closedStore = new RecordingStore();
        Assert.False(new InventoryClearService(closedStore, closed.Gate).TryClear(closed.Client, closed.Room));
        Assert.Equal(0, closedStore.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmationControlsTheClearWorkflow(bool confirmed)
    {
        var inventory = new RecordingClearService();
        var command = new Plus.HabboHotel.Rooms.Chat.Commands.User.EmptyItems(inventory);
        var context = Context();

        command.Execute(context.Client, context.Room, confirmed ? ["yes"] : []);

        Assert.Equal(confirmed ? 1 : 0, inventory.Calls);
    }

    [RoomComponentDatabaseFact]
    public async Task LoaderAndClearStorePreserveOwnerRoomPredicatesAndRollback()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_inventory_" + Guid.NewGuid().ToString("N");
        var builder = new MySqlConnectionStringBuilder(root) { Database = "", Pooling = false };
        await using var server = new MySqlConnection(builder.ConnectionString);
        await server.OpenAsync();
        await server.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4");
        try
        {
            builder.Database = schema;
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                CREATE TABLE users (id INT PRIMARY KEY);
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL,
                    base_item INT UNSIGNED NOT NULL, extra_data TEXT NOT NULL, limited_number INT UNSIGNED NOT NULL,
                    limited_stack INT UNSIGNED NOT NULL);
                CREATE TABLE items_groups (id INT UNSIGNED PRIMARY KEY, group_id INT NOT NULL);
                INSERT INTO users VALUES (1),(2);
                INSERT INTO items VALUES
                    (10,1,0,100,'legacy-a',4,40),(11,1,0,999,'unknown',5,50),(12,1,0,101,'legacy-b',6,60),
                    (13,1,9,100,'room',7,70),(14,2,0,100,'other',8,80);
                INSERT INTO items_groups VALUES (10,3);
                """);
            var definitions = new Definitions(
                new ItemDefinition { Id = 100, Type = ItemType.Floor },
                new ItemDefinition { Id = 101, Type = ItemType.Wall });
            var database = new TestDatabase(builder.ConnectionString);

            var loaded = await new FurnitureInventoryLoader(database, definitions).Load(1);

            Assert.Equal(new uint[] { 10, 12 }, loaded.Select(item => item.Id));
            Assert.Equal(new uint[] { 4, 6 }, loaded.Select(item => item.UniqueNumber));
            Assert.Equal(new uint[] { 40, 60 }, loaded.Select(item => item.UniqueSeries));
            Assert.All(loaded, item => Assert.Equal("", item.ExtraData.Serialize()));

            var store = new InventoryClearStore(database);
            store.DeleteAll(1);
            Assert.Equal(new uint[] { 13, 14 }, await connection.QueryAsync<uint>("SELECT id FROM items ORDER BY id"));

            await connection.ExecuteAsync("DELETE FROM users WHERE id=2");
            var missingOwner = Context(2, 14);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
                new InventoryClearService(store, missingOwner.Gate).TryClear(missingOwner.Client, missingOwner.Room)));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE id=14 AND user_id=2 AND room_id=0"));
            Assert.NotNull(missingOwner.Habbo.Inventory.Furniture.GetItem(14));
            Assert.Empty(missingOwner.Sent);
        }
        finally
        {
            await server.ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static TestContext Context(int userId = 1, uint itemId = 7)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9;
        room.OwnerName = "owner";
        room.Type = "private";
        room.UsersWithRights = [];
        var users = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, users);
        var definition = new ItemDefinition { Id = 100, Type = ItemType.Floor };
        var inventoryItem = new InventoryItem { Id = itemId, OwnerId = (uint)userId, Definition = definition };
        var habbo = new Habbo
        {
            Id = userId,
            Username = "owner",
            CurrentRoom = room,
            Inventory = new InventoryComponent { Furniture = new([inventoryItem], []) }
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var roomUser = new RoomUser(habbo.Id, room.Id, 1, room) { UserId = habbo.Id };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(roomUser, client);
        var entries = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(users)!;
        entries[roomUser.VirtualId] = roomUser;
        var events = new List<string>();
        return new(room, roomUser, habbo, client, sent, events, new RecordingGate(events));
    }

    private sealed record TestContext(Room Room, RoomUser RoomUser, Habbo Habbo, GameClient Client,
        List<(uint Header, byte[] Payload)> Sent, List<string> Events, RecordingGate Gate);

    private sealed class RecordingStore(Action? before = null) : IInventoryClearStore
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }

        public void DeleteAll(int userId)
        {
            Calls++;
            before?.Invoke();
            if (Failure != null) throw Failure;
        }
    }

    private sealed class RecordingClearService : IInventoryClearService
    {
        public int Calls { get; private set; }
        public bool TryClear(GameClient session, Room room) { Calls++; return true; }
    }

    private sealed class RecordingGate(List<string> events) : IAccountSessionGate
    {
        public long Begin() => 0;
        public Task<IDisposable> EnterAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult(Enter(userId));
        public Task<IDisposable> EnterManyAsync(IEnumerable<int> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDisposable>(new Release(events));
        public IDisposable Enter(int userId) { events.Add("gate-enter"); return new Release(events); }
        public void Revoke(int userId) { }
        public bool IsRevoked(int userId, long loginStarted) => false;
    }

    private sealed class Release(List<string> events) : IDisposable
    {
        public void Dispose() => events.Add("gate-exit");
    }

    private sealed class Definitions(params ItemDefinition[] definitions) : IItemDataManager
    {
        public Dictionary<uint, ItemDefinition> Items { get; } = definitions.ToDictionary(definition => definition.Id);
        public Dictionary<int, uint> Gifts { get; } = [];
        public void Init() { }
        public ItemDefinition? GetItemByName(string name) => null;
    }

    private sealed class TestDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
