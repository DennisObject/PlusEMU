using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class WiredRewardStoreDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void FinalClaimWriteFailureRollsBackBadgeAndQuotaThenRetryCommitsOnce()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            Database = "information_schema", AllowZeroDateTime = true, ConvertZeroDateTime = true
        };
        var schema = "task_wired_reward_" + Guid.NewGuid().ToString("N")[..12];
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        admin.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            options.Database = schema;
            var database = new HabbiconDatabaseTests.TestDatabase(options.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("""
                CREATE TABLE rooms (id INT UNSIGNED PRIMARY KEY, owner VARCHAR(20)) ENGINE=InnoDB;
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, user_id INT UNSIGNED, room_id INT UNSIGNED) ENGINE=InnoDB;
                CREATE TABLE user_badges (user_id INT, badge_id VARCHAR(35), badge_slot INT,
                    UNIQUE KEY user_badge (user_id,badge_id)) ENGINE=InnoDB;
                CREATE TABLE badge_definitions (code VARCHAR(35) PRIMARY KEY, required_right VARCHAR(100)) ENGINE=InnoDB;
                CREATE TABLE wired_reward_state (item_id INT UNSIGNED PRIMARY KEY, claims LONGTEXT) ENGINE=InnoDB;
                INSERT INTO rooms VALUES (42,'7');
                INSERT INTO items VALUES (100,7,42);
                INSERT INTO badge_definitions VALUES ('TEST_BADGE','');
                CREATE TRIGGER reject_claim BEFORE UPDATE ON wired_reward_state FOR EACH ROW
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced final claim failure';
                """);
            var store = new WiredRewardStore(database);
            var box = new Item { Id = 100, OwnerId = 7, RoomId = 42 };
            var habbo = new Habbo { Id = 7, Access = UserAccess.Empty };
            var config = WiredRewards.Defaults() with { IntParams = [0,0,1,1,0], Text = "0,TEST_BADGE,100" };

            var error = Assert.Throws<MySqlException>(() => store.ClaimAndGrant(box, 42, habbo, config, null!, 2208988800));
            Assert.Contains("forced final claim failure", error.Message);
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state"));

            connection.Execute("DROP TRIGGER reject_claim");
            var grant = store.ClaimAndGrant(box, 42, habbo, config, null!, 2208988800);
            Assert.Equal(new WiredRewardGrant(4, "TEST_BADGE"), grant);
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges WHERE user_id=7 AND badge_id='TEST_BADGE'"));
            var claims = JsonSerializer.Deserialize<Dictionary<int, WiredRewardClaim>>(
                connection.QuerySingle<string>("SELECT claims FROM wired_reward_state WHERE item_id=100"))!;
            Assert.Equal(1, claims[7].Count);
            Assert.Equal(2208988800, claims[7].LastClaimUnix);
            Assert.Equal(new[] { "TEST_BADGE" }, claims[7].ReceivedCodes);
            Assert.Equal(1, store.ClaimAndGrant(box, 42, habbo, config, null!, 2208988900).Reason);
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges"));
        }
        finally
        {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
