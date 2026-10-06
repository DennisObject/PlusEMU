using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Bots;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogBotPurchaseDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void BotDeliveryCommitsAndRollsBackWithTheWalletTransaction()
    {
        var root = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        {
            Database = "",
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var admin = new MySqlConnection(root.ConnectionString);
        admin.Open();
        var schema = "task_catalog_bot_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            var options = new MySqlConnectionStringBuilder(root.ConnectionString) { Database = schema };
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE users (id INT PRIMARY KEY,credits INT NOT NULL,activity_points INT NOT NULL,vip_points INT NOT NULL);
                CREATE TABLE bots (id INT UNSIGNED NOT NULL AUTO_INCREMENT,room_id INT UNSIGNED NOT NULL DEFAULT 0,user_id INT UNSIGNED NOT NULL DEFAULT 0,ai_type ENUM('generic','bartender','pet') NOT NULL DEFAULT 'generic',name VARCHAR(100) NOT NULL,motto VARCHAR(120) NOT NULL,look TEXT NOT NULL,x INT NOT NULL DEFAULT 0,y INT NOT NULL DEFAULT 0,z INT NOT NULL DEFAULT 0,rotation INT NOT NULL DEFAULT 0,walk_mode ENUM('stand','freeroam','specified_range') NOT NULL DEFAULT 'freeroam',min_x INT NOT NULL DEFAULT 0,min_y INT NOT NULL DEFAULT 0,max_x INT NOT NULL DEFAULT 0,max_y INT NOT NULL DEFAULT 0,effect INT NOT NULL DEFAULT 0,gender VARCHAR(5) NOT NULL DEFAULT 'M',dance INT NOT NULL DEFAULT 0,automatic_chat ENUM('false','true') NOT NULL DEFAULT 'false',speaking_interval INT NOT NULL DEFAULT 30,mix_sentences BOOL NOT NULL DEFAULT FALSE,chat_bubble INT NOT NULL DEFAULT 2,PRIMARY KEY(id),KEY user_id(user_id),KEY room_id(room_id),KEY ai_type(ai_type));
                CREATE TABLE club_credit_spending (id INT AUTO_INCREMENT PRIMARY KEY,user_id INT NOT NULL,credits INT NOT NULL,spent_at DATETIME(6) NOT NULL);
                CREATE TABLE catalog_items (id INT PRIMARY KEY,limited_sells INT NOT NULL,limited_stack INT NOT NULL);
                INSERT INTO catalog_items VALUES (50,0,2);
                INSERT INTO users VALUES (42,100,50,25);
                """);
            var database = new ProbeDatabase(options.ConnectionString);
            var now = new DateTimeOffset(2040, 2, 3, 4, 5, 6, TimeSpan.Zero);
            var rewards = new ClubRewards(database, null!, null!, new Settings(), null!, new FixedClock(now), null!);
            var store = new CatalogBotPurchaseStore();
            var preset = new CatalogBot { Id = 50, Name = "Catalog Bot", Motto = "A bot motto", Figure = "hd-180-1", Gender = "M", AiType = "generic" };
            var habbo = Habbo(now);
            Bot? bot = null;

            Assert.True(rewards.Charge(habbo, 10, deliver: (db, transaction) =>
            {
                Assert.Equal(1, CatalogLimitedStock.Reserve(db, transaction, 50));
                bot = store.Create(db, transaction, preset, habbo.Id);
                Assert.Equal(100, habbo.Credits);

                return true;
            }));

            Assert.Equal(90, habbo.Credits);
            Assert.NotNull(bot);
            var saved = connection.QuerySingle<BotRow>("SELECT user_id AS UserId,name AS Name,motto AS Motto,look AS Look,gender AS Gender,ai_type AS AiType FROM bots");
            Assert.Equal((42, "Catalog Bot", "A bot motto", "hd-180-1", "M", "generic"),
                (saved.UserId, saved.Name, saved.Motto, saved.Look, saved.Gender, saved.AiType));
            Assert.Equal(90, connection.ExecuteScalar<int>("SELECT credits FROM users WHERE id=42"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT limited_sells FROM catalog_items WHERE id=50"));
            var spending = connection.QuerySingle<SpendingRow>("SELECT credits AS Credits,spent_at AS SpentAt FROM club_credit_spending");
            Assert.Equal((10, now.UtcDateTime), (spending.Credits, spending.SpentAt));

            connection.Execute("DELETE FROM bots; DELETE FROM club_credit_spending; UPDATE users SET credits=100 WHERE id=42; UPDATE catalog_items SET limited_sells=0 WHERE id=50; CREATE TRIGGER reject_bot AFTER INSERT ON bots FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced';");
            habbo.Credits = 100;

            Assert.Throws<MySqlException>(() => rewards.Charge(habbo, 10,
                deliver: (db, transaction) =>
                {
                    Assert.Equal(1, CatalogLimitedStock.Reserve(db, transaction, 50));
                    store.Create(db, transaction, preset, habbo.Id);

                    return true;
                }));

            Assert.Equal(100, habbo.Credits);
            Assert.Equal(100, connection.ExecuteScalar<int>("SELECT credits FROM users WHERE id=42"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bots"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM club_credit_spending"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT limited_sells FROM catalog_items WHERE id=50"));
        }
        finally
        {
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static Habbo Habbo(DateTimeOffset now) => new()
    {
        Id = 42,
        Credits = 100,
        Duckets = 50,
        Diamonds = 25,
        Access = UserAccess.Create([], membership: new ClubMembership(now.AddDays(1), now.AddDays(-1), now.AddDays(-1)))
    };

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private sealed class BotRow
    {
        public int UserId
        {
            get; set;
        }
        public string Name { get; set; } = "";
        public string Motto { get; set; } = "";
        public string Look { get; set; } = "";
        public string Gender { get; set; } = "";
        public string AiType { get; set; } = "";
    }

    private sealed class SpendingRow
    {
        public int Credits
        {
            get; set;
        }
        public DateTime SpentAt
        {
            get; set;
        }
    }

    private sealed class Settings : ISettingsManager
    {
        public string TryGetValue(string value) => "10";
        public string? GetOptionalValue(string key) => "10";
        public Task Reload() => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
