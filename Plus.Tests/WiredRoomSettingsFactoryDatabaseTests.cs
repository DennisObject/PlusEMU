using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
public sealed class WiredRoomSettingsFactoryDatabaseTests
{
    [HousekeepingDatabaseFact]
    public void RuntimeFactoryLoadsAndSavesAgainstProductionConnectionOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING")!;
        var options = new MySqlConnectionStringBuilder(connectionString);
        Assert.True(options.Database.StartsWith("task_housekeeping_tests_", StringComparison.Ordinal));
        Assert.True(options.AllowZeroDateTime);
        Assert.True(options.ConvertZeroDateTime);
        var database = new HabbiconDatabaseTests.TestDatabase(connectionString);
        using var connection = database.Connection();
        connection.Open();
        var migrationPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Database/Migrations/18_AddWiredRoomSettings.sql"));
        connection.Execute(File.ReadAllText(migrationPath));
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var username = "wired_factory_" + suffix;
        connection.Execute("INSERT INTO users (username, auth_ticket, `rank`, credits, activity_points, vip_points, mail, ip_last, online) VALUES (@username, '', 1, 0, 0, 0, '', '', 0)", new { username });
        var userId = connection.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        var model = connection.QueryFirst<string>("SELECT id FROM room_models LIMIT 1");
        connection.Execute("INSERT INTO rooms (owner, caption, model_name) VALUES (@owner, 'Wired factory probe', @model)", new { owner = userId.ToString(), model });
        var roomId = connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()");

        try {
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.Id = roomId;
            room.OwnerId = userId;
            room.OwnerName = username;
            room.Type = "private";
            room.UsersWithRights = [];
            var factory = new WiredRoomSettingsFactory(new DatabaseWiredRoomSettingsStore(database));
            var wired = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, factory,
                new Plus.HabboHotel.Items.Wired.Configuration.WiredConfigurationStore(database), database, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, wired);
            var client = new FlashGameClient(TestGameServer.Instance, new Plus.Communication.Flash.FlashPacketFactory(), TestLogging.GameClient);
            client.SetHabbo(new Habbo { Id = userId, Username = username, CurrentRoom = room, Access = EditorTestSupport.Access([]) });

            Assert.Equal(new(), room.GetWired().Settings.Snapshot);
            Assert.True(room.GetWired().Settings.TrySave(client, 15, 14, "Europe/Berlin", out var error), error);
            Assert.Equal(new(15, 14, "Europe/Berlin"), factory.Create(room).Snapshot);
        }
        finally {
            connection.Execute("DELETE FROM room_wired_settings WHERE room_id=@roomId; DELETE FROM rooms WHERE id=@roomId; DELETE FROM users WHERE id=@userId", new { roomId, userId });
        }
    }
}
