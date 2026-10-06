using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Moderation;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorActionDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void RoomSettingsAndPromotionDeletionCommitOrRollbackTogether()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "moderator_action_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE rooms (id INT UNSIGNED PRIMARY KEY, caption VARCHAR(100), description VARCHAR(100),
                    state ENUM('open','locked','password','invisible') NOT NULL, tags VARCHAR(100));
                CREATE TABLE room_promotions (room_id INT UNSIGNED PRIMARY KEY);
                CREATE TABLE users (id INT PRIMARY KEY, time_muted DOUBLE NOT NULL);
                CREATE TABLE user_info (user_id INT PRIMARY KEY, cautions INT NOT NULL);
                INSERT INTO rooms VALUES (42, 'Original', 'Description', 'password', 'bad');
                INSERT INTO room_promotions VALUES (42);
                INSERT INTO users VALUES (7, 0);
                INSERT INTO user_info VALUES (7, 2);
                """);
            var options = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = schema };
            var store = new ModeratorActionStore(new ProbeDatabase(options.ConnectionString));
            foreach (var (rename, locked, expectedCaption, expectedState) in new[]
                { (false, false, "Original", "password"), (false, true, "Original", "locked"),
                  (true, false, ModeratorActionService.InappropriateRoomText, "password"),
                  (true, true, ModeratorActionService.InappropriateRoomText, "locked") })
            {
                connection.Execute("UPDATE rooms SET caption='Original',description='Description',state='password',tags='bad'");
                store.ModerateRoom(42, rename, locked, false);
                Assert.Equal((expectedCaption, expectedState, ""), connection.QuerySingle<(string, string, string)>("SELECT caption,state,tags FROM rooms"));
            }
            connection.Execute("UPDATE rooms SET caption='Original',description='Description',state='password',tags='bad'");
            connection.Execute("CREATE TRIGGER reject_promotion_delete BEFORE DELETE ON room_promotions FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced failure'");
            Assert.Throws<MySqlException>(() => store.ModerateRoom(42, true, true, true));
            Assert.Equal(("Original", "Description", "password", "bad"), connection.QuerySingle<(string, string, string, string)>("SELECT caption,description,state,tags FROM rooms"));
            Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM room_promotions"));
            connection.Execute("DROP TRIGGER reject_promotion_delete");
            store.ModerateRoom(42, true, true, true);
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM room_promotions"));
            Assert.Equal("locked", connection.QuerySingle<string>("SELECT state FROM rooms"));
            store.AddCaution(7);
            Assert.Equal(3, connection.QuerySingle<int>("SELECT cautions FROM user_info WHERE user_id=7"));
            var seconds = (long)int.MaxValue * 60;
            store.SetMute(7, seconds);
            Assert.Equal((double)seconds, connection.QuerySingle<double>("SELECT time_muted FROM users WHERE id=7"));
        }
        finally
        {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
