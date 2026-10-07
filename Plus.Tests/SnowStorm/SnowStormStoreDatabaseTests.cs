using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests.SnowStorm;

public class SnowStormStoreDatabaseTests
{
    private const string Variable = "PLUS_SNOWWAR_TEST_CONNECTION_STRING";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [SnowStormDatabaseFact]
    public void GamesTokensScoresAndLeaderboardsRoundTrip()
    {
        WithSchema(connectionString =>
        {
            var store = new SnowStormStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            // Two free games a day, then bought games; the purchase debits before it credits, in one transaction.
            Assert.Equal(new SnowStormAccount(0, 0, 0), store.GetAccount(1, Today));
            Assert.True(store.TryConsumeGame(1, Today, 2));
            Assert.True(store.TryConsumeGame(1, Today, 2));
            Assert.False(store.TryConsumeGame(1, Today, 2));
            var poor = new Habbo { Id = 2, Credits = 5 };
            Assert.Null(store.Purchase(poor, 1));
            Assert.Equal(5, poor.Credits);
            var habbo = new Habbo { Id = 1, Credits = 50, Duckets = 7 };
            Assert.Equal(10, store.Purchase(habbo, 1)!.Games);
            Assert.Equal(40, habbo.Credits);
            Assert.True(store.TryConsumeGame(1, Today, 2));
            Assert.Equal(new SnowStormAccount(0, 2, 9), store.GetAccount(1, Today));
            store.RefundGame(1);
            store.RefundGame(3);
            Assert.Equal(new SnowStormAccount(0, 2, 10), store.GetAccount(1, Today));
            Assert.Equal(1, store.GetAccount(3, Today).Tokens);
            Assert.Equal(0, store.GetAccount(1, Today.AddDays(1)).FreeGamesUsedToday);
            Assert.Null(store.Purchase(habbo, 99));
            Assert.Equal(["GET_SNOWWAR_TOKENS", "GET_SNOWWAR_TOKENS2", "GET_SNOWWAR_TOKENS3"], store.GetOffers().Select(offer => offer.LocalizationId));

            using (var connection = new MySqlConnection(connectionString)) {
                Assert.Equal((40, 7), connection.QuerySingle<(int, int)>("SELECT credits, activity_points FROM users WHERE id = 1"));
            }

            var week = SnowStormStore.WeekStart(Now);
            store.RecordScores(week, [(1, 30), (2, 50), (3, 10)]);
            store.RecordScores(week, [(1, 5)]);
            store.RecordScores(week.AddDays(-7), [(4, 100)]);
            Assert.Equal(2, store.GetAccount(1, Today).GamesPlayed);
            Assert.Equal(new Dictionary<int, int> { [1] = 35, [4] = 100 }, store.GetTotalScores([1, 4]));

            var total = store.LoadLeaderboard(new(SnowStormLeaderboardKind.Total, 1, 0, 1, 8, 50), Now);
            Assert.Equal([(4, 100, 1), (2, 50, 2), (1, 35, 3), (3, 10, 4)], total.Entries.Select(entry => (entry.UserId, entry.Score, entry.Rank)));
            Assert.Equal(("Dee", "look4", "f"), (total.Entries[0].Name, total.Entries[0].Figure, total.Entries[0].Gender));
            Assert.Equal(4, total.TotalListSize);
            Assert.Null(total.Week);

            var around = store.LoadLeaderboard(new(SnowStormLeaderboardKind.Total, 3, 0, -1, 2, 2), Now);
            Assert.Equal([3, 4], around.Entries.Select(entry => entry.Rank));

            var weekly = store.LoadLeaderboard(new(SnowStormLeaderboardKind.Weekly, 1, 0, 1, 8, 50), Now);
            Assert.Equal([2, 1, 3], weekly.Entries.Select(entry => entry.UserId));
            Assert.Equal(new SnowStormLeaderboardWeek(2026, 41, 1, 0, SnowStormStore.MinutesUntilReset(Now)), weekly.Week);
            var lastWeek = store.LoadLeaderboard(new(SnowStormLeaderboardKind.Weekly, 1, 5, 1, 8, 50), Now);
            Assert.Equal((1, 40, 4), (lastWeek.Week!.CurrentOffset, lastWeek.Week.Week, Assert.Single(lastWeek.Entries).UserId));

            var friends = store.LoadLeaderboard(new(SnowStormLeaderboardKind.Friends, 1, 0, 1, 8, 50), Now);
            Assert.Equal([(2, 1), (1, 2)], friends.Entries.Select(entry => (entry.UserId, entry.Rank)));

            var groups = store.LoadLeaderboard(new(SnowStormLeaderboardKind.TotalGroup, 1, 0, -1, 8, 50), Now);
            Assert.Equal(new SnowStormLeaderboardEntry(7, 45, 1, "Snow", "b1", "g"), Assert.Single(groups.Entries));
            Assert.Equal(7, groups.FavouriteGroupId);
        });
    }

    private static void WithSchema(Action<string> body)
    {
        var server = Environment.GetEnvironmentVariable(Variable)!;
        var schema = "snowwar_tests_" + Guid.NewGuid().ToString("N")[..12];
        // Same date options as Plus.Database: DATE columns then read back as MySqlDateTime, not DateTime.
        var options = new MySqlConnectionStringBuilder(server) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true };

        using (var admin = new MySqlConnection(server)) {
            admin.Execute($"CREATE DATABASE `{schema}`");
        }

        try {
            using (var connection = new MySqlConnection(options.ConnectionString)) {
                connection.Execute("""
                    CREATE TABLE users (id INT PRIMARY KEY, username VARCHAR(125), look CHAR(255), gender ENUM('M','F'), credits INT, activity_points INT, vip_points INT) ENGINE=InnoDB;
                    CREATE TABLE messenger_friendships (user_one_id INT UNSIGNED, user_two_id INT UNSIGNED, relationship INT NOT NULL DEFAULT 0, PRIMARY KEY (user_one_id, user_two_id));
                    CREATE TABLE `groups` (id INT UNSIGNED PRIMARY KEY, name VARCHAR(50), badge VARCHAR(50));
                    CREATE TABLE user_statistics (id INT PRIMARY KEY, groupid INT NOT NULL DEFAULT 0);
                    CREATE TABLE server_settings (`key` VARCHAR(255) PRIMARY KEY, `value` TEXT NOT NULL, description TEXT NOT NULL);
                    INSERT INTO users VALUES (1, 'Ann', 'look1', 'F', 50, 7, 0), (2, 'Bo', 'look2', 'M', 5, 0, 0), (3, 'Cy', 'look3', 'M', 0, 0, 0), (4, 'Dee', 'look4', 'F', 0, 0, 0);
                    INSERT INTO messenger_friendships (user_one_id, user_two_id) VALUES (1, 2), (2, 1);
                    INSERT INTO `groups` VALUES (7, 'Snow', 'b1');
                    INSERT INTO user_statistics VALUES (1, 7), (2, 0), (3, 7), (4, 0);
                    """);
                // The update must be rerunnable.
                var update = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/57_SnowStorm.sql"));
                connection.Execute(update);
                connection.Execute(update);
                Assert.Equal("1", connection.ExecuteScalar<string>("SELECT `value` FROM server_settings WHERE `key` = 'gamecenter.snowwar.enabled'"));
                var rayGuns = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/58_SnowStormRayGuns.sql"));
                connection.Execute(rayGuns);
                connection.Execute(rayGuns);
                Assert.Equal("1", connection.ExecuteScalar<string>("SELECT `value` FROM server_settings WHERE `key` = 'gamecenter.snowwar.raygun.enabled'"));
            }

            body(options.ConnectionString);
        }
        finally {
            using var admin = new MySqlConnection(server);
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }
}

public sealed class SnowStormDatabaseFactAttribute : FactAttribute
{
    public SnowStormDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_SNOWWAR_TEST_CONNECTION_STRING"))) {
            Skip = "Set PLUS_SNOWWAR_TEST_CONNECTION_STRING to a server that can create and drop disposable snowwar_tests_ schemas.";
        }
    }
}
