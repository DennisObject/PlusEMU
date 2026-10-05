using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Rooms.Chat.Logs;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorHistoryDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void NativeUtcHistoryPreservesMicrosecondsAndMissingExitWithProductionOptions()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_history_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root)
            {
                Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true
            }.ConnectionString);
            var entered = DateTimeOffset.FromUnixTimeSeconds(2200000000).AddTicks(1234560);
            var messageTime = entered.AddSeconds(1);
            var clock = new Clock(entered.AddSeconds(2));
            using (var connection = database.Connection())
                connection.Execute("""
                    CREATE TABLE rooms(id INT UNSIGNED PRIMARY KEY,caption VARCHAR(50));
                    CREATE TABLE user_roomvisits(id INT PRIMARY KEY,user_id INT,room_id INT UNSIGNED,
                        entry_timestamp DATETIME(6) NULL,exit_timestamp DATETIME(6) NULL);
                    CREATE TABLE chatlogs(id INT PRIMARY KEY,user_id INT,room_id INT UNSIGNED,
                        timestamp DATETIME(6) NULL,message VARCHAR(50));
                    INSERT INTO rooms VALUES(42,'Room');
                    INSERT INTO user_roomvisits VALUES(1,7,42,@entered,NULL),(2,7,42,NULL,NULL);
                    INSERT INTO chatlogs VALUES(1,9,42,@messageTime,'message'),(2,9,42,NULL,'unknown');
                    """, new { entered = entered.UtcDateTime, messageTime = messageTime.UtcDateTime });
            var chatlogs = new Chatlogs();
            var service = new ModeratorHistoryService(database, null!, new Users(), chatlogs, clock);

            var history = Assert.IsType<ModeratorUserChatlog>(service.GetUserChatlog(7));
            var room = Assert.Single(history.Rooms);
            var entry = Assert.Single(room.Entries);
            Assert.Equal(messageTime, entry.Timestamp);
            Assert.Equal("Author", entry.Username);
            Assert.Equal("message", entry.Message);
            Assert.Equal(1, clock.Reads);
            Assert.Equal(1, chatlogs.Flushes);
            var visits = Assert.IsType<ModeratorUserRoomVisits>(service.GetUserRoomVisits(7));
            Assert.Equal(entered, Assert.Single(visits.Visits).EnteredAt);
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class Users : IModeratorUserLookup
    {
        public Habbo? GetById(int userId) => new Habbo { Id = userId, Username = userId == 7 ? "Target" : "Author" };
    }
    private sealed class Chatlogs : IChatlogManager
    {
        public int Flushes { get; private set; }
        public void FlushAndSave() => Flushes++;
        public void StoreChatlog(ChatlogEntry entry) => throw new InvalidOperationException();
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
