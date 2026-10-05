using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
public sealed class RoomDeletionDatabaseTests : IDisposable
{
    private const int UserId = 936001;
    private const uint RoomId = 936002;
    private const uint ItemId = 936003;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly Room _room;
    private readonly ManagerProxy _manager;
    private readonly RoomDeletionService _service;

    public RoomDeletionDatabaseTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("PLUS_REFACTOR_TEST_CONNECTION_STRING")!;
        if (!new MySqlConnectionStringBuilder(connectionString).Database.StartsWith("task_refactor_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Room deletion tests require a disposable task_refactor_tests_ schema.");
        _database = new(connectionString);
        Dispose();
        using var connection = _database.Connection();
        connection.Execute("""
            INSERT INTO users(id,username,auth_ticket) VALUES (@UserId,'room_cleanup_probe','');
            INSERT INTO users_settings(user_id,home_room) VALUES (@UserId,@RoomId);
            INSERT INTO rooms(id,model_name,owner) VALUES (@RoomId,'probe',@UserId);
            INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES (@ItemId,@UserId,@RoomId,0,'preserved');
            INSERT INTO room_items_moodlight(item_id,enabled,current_preset,preset_one,preset_two,preset_three)
              VALUES (@ItemId,TRUE,1,'#0053F7,200,1','two','three');
            INSERT INTO room_rights(room_id,user_id) VALUES (@RoomId,@UserId);
            INSERT INTO user_favorites(user_id,room_id) VALUES (@UserId,@RoomId);
            INSERT INTO user_roomvisits(user_id,room_id,entry_timestamp,exit_timestamp) VALUES (@UserId,@RoomId,0,0);
            """, Values);
        _room = new Room(new RoomData { Id = RoomId }, [], TestLogging.Navigation, TestLogging.Logger);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_room, new RoomItemHandling(_room, new RoomItemStore(_database), TestRoomItemMetadataStore.Instance));
        var manager = DispatchProxy.Create<IRoomManager, ManagerProxy>();
        _manager = (ManagerProxy)(object)manager;
        _service = new(new HousekeepingActionTests.FakeClients(), manager, _database);
    }

    [TradingLockDatabaseFact]
    public void CommittedCleanupReturnsPersistedItemsAndPreservesTheirMoodlight()
    {
        _service.Delete(_room);
        using var connection = _database.Connection();
        Assert.Equal(0, connection.QuerySingle<int>("SELECT room_id FROM items WHERE id=@ItemId", Values));
        Assert.Equal(0, connection.QuerySingle<int>("SELECT home_room FROM users_settings WHERE user_id=@UserId", Values));
        Assert.Equal("#0053F7,200,1", connection.QuerySingle<string>("SELECT preset_one FROM room_items_moodlight WHERE item_id=@ItemId", Values));
        Assert.Equal(0, RemainingRoomRows(connection));
        Assert.Equal(1, _manager.Unloads);
    }

    [TradingLockDatabaseFact]
    public void FailedCleanupRollsBackAllRowsAndDoesNotUnloadTheRoom()
    {
        using var connection = _database.Connection();
        connection.Execute($"CREATE TRIGGER refactor_room_cleanup_failure BEFORE DELETE ON rooms FOR EACH ROW BEGIN IF OLD.id={RoomId} THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='rollback probe'; END IF; END");
        Assert.Throws<MySqlException>(() => _service.Delete(_room));
        Assert.Equal((int)RoomId, connection.QuerySingle<int>("SELECT room_id FROM items WHERE id=@ItemId", Values));
        Assert.Equal((int)RoomId, connection.QuerySingle<int>("SELECT home_room FROM users_settings WHERE user_id=@UserId", Values));
        Assert.Equal("#0053F7,200,1", connection.QuerySingle<string>("SELECT preset_one FROM room_items_moodlight WHERE item_id=@ItemId", Values));
        Assert.Equal(4, RemainingRoomRows(connection));
        Assert.Equal(0, _manager.Unloads);
    }

    private static object Values => new { UserId, RoomId, ItemId };
    private static int RemainingRoomRows(System.Data.IDbConnection connection) => connection.QuerySingle<int>("""
        SELECT (SELECT COUNT(*) FROM rooms WHERE id=@RoomId) + (SELECT COUNT(*) FROM room_rights WHERE room_id=@RoomId)
          + (SELECT COUNT(*) FROM user_favorites WHERE room_id=@RoomId) + (SELECT COUNT(*) FROM user_roomvisits WHERE room_id=@RoomId)
        """, Values);

    public void Dispose()
    {
        using var connection = _database.Connection();
        connection.Execute("""
            DROP TRIGGER IF EXISTS refactor_room_cleanup_failure;
            DELETE FROM room_items_moodlight WHERE item_id=@ItemId;
            DELETE FROM items WHERE id=@ItemId;
            DELETE FROM room_rights WHERE room_id=@RoomId;
            DELETE FROM user_favorites WHERE room_id=@RoomId;
            DELETE FROM user_roomvisits WHERE room_id=@RoomId;
            DELETE FROM rooms WHERE id=@RoomId;
            DELETE FROM users_settings WHERE user_id=@UserId;
            DELETE FROM users WHERE id=@UserId;
            """, Values);
    }

    private class ManagerProxy : DispatchProxy
    {
        public int Unloads { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "UnloadRoom") throw new NotSupportedException(method?.Name);
            Assert.Equal(RoomId, args![0]);
            Unloads++;
            return null;
        }
    }
}
