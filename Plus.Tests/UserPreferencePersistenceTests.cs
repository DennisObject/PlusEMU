using System.Data;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Communication.Packets.Incoming.Preferences;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserPreferencePersistenceTests
{
    [Fact]
    public async Task FailedWritesLeaveBothLivePreferencesAndPacketsUnchanged()
    {
        var user = new Habbo { Id = 7, HomeRoom = 20, ChatPreference = false };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System);
        var navigator = new NavigatorManager(new FailingDatabase(), TestLogging.For<NavigatorManager>(), new Rooms());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetChatPreferenceEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateNavigatorSettingsEvent(navigator).Parse(session, HabbiconTestSupport.Incoming(42)));
        Assert.False(user.ChatPreference);
        Assert.Equal(20u, user.HomeRoom);
        Assert.Empty(sent);
    }

    [RoomComponentDatabaseFact]
    public async Task SettingsWritesUseCanonicalUserIdAndPublishOnlyExistingRows()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_preferences_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);
            using (var connection = database.Connection())
                connection.Execute("CREATE TABLE users_settings(user_id INT PRIMARY KEY, home_room INT UNSIGNED NOT NULL, chat_preference BOOL NOT NULL); INSERT INTO users_settings VALUES(7,20,false)");
            var user = new Habbo { Id = 7, HomeRoom = 20 };
            var (session, sent) = HabbiconTestSupport.Client(user);
            var profiles = new UserProfileService(null!, null!, null!, null!, database, TimeProvider.System);
            var navigator = new NavigatorManager(database, TestLogging.For<NavigatorManager>(), new Rooms());
            await new SetChatPreferenceEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(true));
            await new UpdateNavigatorSettingsEvent(navigator).Parse(session, HabbiconTestSupport.Incoming(42));
            Assert.True(user.ChatPreference);
            Assert.Equal(42u, user.HomeRoom);
            Assert.Equal(ServerPacketHeader.NavigatorSettingsComposer, Assert.Single(sent).Header);
            using var verify = database.Connection();
            Assert.Equal((42u, true), verify.QuerySingle<(uint, bool)>("SELECT home_room,chat_preference FROM users_settings WHERE user_id=7"));
            verify.Execute("DELETE FROM users_settings WHERE user_id=7");
            sent.Clear();
            await Assert.ThrowsAsync<DBConcurrencyException>(() => profiles.SetChatPreference(session, false));
            await Assert.ThrowsAsync<DBConcurrencyException>(() => navigator.SaveHomeRoom(session, 43));
            Assert.True(user.ChatPreference);
            Assert.Equal(42u, user.HomeRoom);
            Assert.Empty(sent);
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class Rooms : IRoomDataLoader
    {
        public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data)
        {
            data = new RoomData { Id = roomId };
            return true;
        }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId) => [];
    }
    private sealed class FailingDatabase : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => throw new InvalidOperationException("Write failed");
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
