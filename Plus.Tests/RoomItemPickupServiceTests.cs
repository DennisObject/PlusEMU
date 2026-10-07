using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task PickupHandlerConsumesJunkAndItemIdBeforeDelegation()
    {
        var calls = new List<uint>();
        var service = Proxy<IRoomItemPickupService>((_, args) =>
        {
            calls.Add((uint)args[1]!);

            return Task.CompletedTask;
        });
        var packet = ClientPacket(123, 456);
        await new PickupObjectEvent(service).Parse(_client, packet);
        Assert.Equal(456u, Assert.Single(calls));
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public async Task OwnerPickupCommitsBeforeRoomInventoryAndQuestPublication()
    {
        Viewer();
        var item = Add(30, 1, 1);
        _client.Sent.Clear();
        var progressed = false;
        var store = new PickupStore(request =>
        {
            Assert.Equal(new RoomItemPickup(30, RoomId, 7, 7, InteractionType.None, false), request);
            Assert.Same(item, _room.GetRoomItemHandler().GetItem(item.Id));
            Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
            Assert.Empty(_client.Sent);
            Assert.False(progressed);

            return true;
        });
        var quests = Proxy<IQuestManager>((_, _) => { progressed = true; return null; });
        var pickup = new RoomItemPickupService(Proxy<IGameClientManager>((_, _) => null), quests, store);

        await pickup.PickUp(_client, item.Id);
        await pickup.PickUp(_client, item.Id);

        Assert.Single(store.Calls);
        Assert.Null(_room.GetRoomItemHandler().GetItem(item.Id));
        Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
        Assert.True(progressed);
        Assert.Contains(ServerPacketHeader.FurniListUpdateComposer, _client.Sent);
    }

    [Fact]
    public async Task FailedPickupKeepsRoomInventoryAndPacketsUnchanged()
    {
        Viewer();
        var item = Add(30, 1, 1);
        _client.Sent.Clear();
        var store = new PickupStore(_ => false);
        var pickup = new RoomItemPickupService(Proxy<IGameClientManager>((_, _) => null),
            Proxy<IQuestManager>((_, _) => throw new InvalidOperationException("Failed pickup must not progress quest")), store);

        await pickup.PickUp(_client, item.Id);

        Assert.Same(item, _room.GetRoomItemHandler().GetItem(item.Id));
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task StaffTakePersistsAndPublishesTheSameNewOwner()
    {
        Viewer();
        var item = Add(30, 1, 1);
        item.OwnerId = 99;
        item.UserId = 99;
        _client.GetHabbo().Access = EditorTestSupport.Access([PermissionKeys.RoomItemTake]);
        var store = new PickupStore(request =>
        {
            Assert.Equal((99, 7), (request.OwnerId, request.RecipientId));

            return true;
        });
        await new RoomItemPickupService(Proxy<IGameClientManager>((_, _) => null),
            Proxy<IQuestManager>((_, _) => null), store).PickUp(_client, item.Id);

        Assert.Equal(7u, _client.GetHabbo().Inventory.Furniture.GetItem(item.Id)!.OwnerId);
    }

    [Fact]
    public async Task RoomOwnerEjectsAnOfflineOwnersItemWithoutTakingIt()
    {
        Viewer();
        var item = Add(30, 1, 1);
        item.OwnerId = 99;
        item.UserId = 99;
        var store = new PickupStore(request =>
        {
            Assert.Equal((99, 99), (request.OwnerId, request.RecipientId));

            return true;
        });
        await new RoomItemPickupService(Proxy<IGameClientManager>((_, _) => null),
            Proxy<IQuestManager>((_, _) => null), store).PickUp(_client, item.Id);
        Assert.Null(_room.GetRoomItemHandler().GetItem(item.Id));
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
    }

    [Fact]
    public async Task PickupDeniesUnrelatedOwnerAndPostitWithoutPersistence()
    {
        Viewer();
        var item = Add(30, 1, 1);
        item.OwnerId = 99;
        item.UserId = 99;
        _client.GetHabbo().Id = 8;
        _client.GetHabbo().Username = "intruder";
        var store = new PickupStore(_ => throw new InvalidOperationException("Denied pickup must not write"));
        var service = new RoomItemPickupService(Proxy<IGameClientManager>((_, _) => null),
            Proxy<IQuestManager>((_, _) => null), store);
        await service.PickUp(_client, item.Id);
        _client.GetHabbo().Id = 7;
        _client.GetHabbo().Username = "owner";
        item.Definition.InteractionType = InteractionType.Postit;
        await service.PickUp(_client, item.Id);
        Assert.Same(item, _room.GetRoomItemHandler().GetItem(item.Id));
        Assert.Empty(store.Calls);
    }

    private sealed class PickupStore(Func<RoomItemPickup, bool> persist) : IRoomItemPickupStore
    {
        public List<RoomItemPickup> Calls { get; } = [];
        public bool PickUp(RoomItemPickup request)
        {
            Calls.Add(request);

            return persist(request);
        }
    }
}

public class RoomItemPickupStoreTests
{
    [RoomComponentDatabaseFact]
    public void PickupIsExactAndDependentCleanupRollsBackWithOwnership()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_refactor_tests_pickup_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var builder = new MySqlConnectionStringBuilder(root) { Database = schema };
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE items(id INT PRIMARY KEY, room_id INT NOT NULL, user_id INT NOT NULL, extra_data TEXT NOT NULL);
                CREATE TABLE room_items_moodlight(item_id INT PRIMARY KEY);
                CREATE TABLE room_items_toner(id INT PRIMARY KEY);
                INSERT INTO items VALUES(30,42,99,'preserved');
                INSERT INTO room_items_moodlight VALUES(30);
                CREATE TRIGGER reject_cleanup BEFORE DELETE ON room_items_moodlight
                    FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced pickup rollback';
                """);
            var store = new RoomItemPickupStore(new PickupDatabase(builder.ConnectionString));
            var request = new RoomItemPickup(30, 42, 99, 7, InteractionType.Moodlight, false);
            Assert.Throws<MySqlException>(() => store.PickUp(request));
            Assert.Equal((42, 99), connection.QuerySingle<(int, int)>("SELECT room_id,user_id FROM items"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_items_moodlight"));
            connection.Execute("DROP TRIGGER reject_cleanup");
            Assert.False(store.PickUp(request with { RoomId = 43 }));
            Assert.False(store.PickUp(request with { OwnerId = 98 }));
            Assert.True(store.PickUp(request));
            Assert.False(store.PickUp(request));
            Assert.Equal((0, 7), connection.QuerySingle<(int, int)>("SELECT room_id,user_id FROM items"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_items_moodlight"));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class PickupDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
