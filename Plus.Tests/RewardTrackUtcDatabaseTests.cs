using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Plus.Tests;

public class RewardTrackUtcDatabaseTests
{
    [RewardTrackUtcDatabaseFact]
    public async Task MigratedWindowsMaterializeWithProductionFlagsAndBoundsAreExact()
    {
        await WithSchema(async connectionString =>
        {
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                connection.Execute("CREATE TABLE badge_definitions (code VARCHAR(64) PRIMARY KEY, required_right VARCHAR(64) NOT NULL DEFAULT '') ENGINE=InnoDB");
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/15_IntroductionRewardTrack.sql")));
                connection.Execute("INSERT INTO reward_tracks (id, theme, sort_order, starts_at, ends_at, has_premium, enabled) VALUES ('early', 'blue', 1, 1700000000, 2147483647, 0, 1), ('late', 'blue', 2, 1700001000, 0, 0, 1), ('neg', 'blue', 3, -5, 0, 0, 1)");
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/35_UseUtcRewardTrackTimes.sql")));
            }

            var manager = new RewardTrackManager(NullLogger<RewardTrackManager>.Instance, new HabbiconDatabaseTests.TestDatabase(connectionString), NoBadges(), new FixedTimeProvider(FixedTimeProvider.Epoch));
            await manager.Start();
            var tracks = Loaded(manager).ToDictionary(track => track.Id);

            Assert.Null(tracks["introduction"].StartsAt);
            Assert.Null(tracks["introduction"].EndsAt);
            Assert.Equal(new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero), tracks["early"].StartsAt);
            Assert.Equal(new DateTimeOffset(2038, 1, 19, 3, 14, 7, TimeSpan.Zero), tracks["early"].EndsAt);
            Assert.Null(tracks["neg"].StartsAt);
            Assert.Null(tracks["neg"].EndsAt);

            var start = tracks["early"].StartsAt!.Value;
            var end = tracks["early"].EndsAt!.Value;
            Assert.False(tracks["early"].IsActiveAt(start.AddTicks(-1)));
            Assert.True(tracks["early"].IsActiveAt(start));
            Assert.True(tracks["early"].IsActiveAt(end.AddTicks(-1)));
            Assert.False(tracks["early"].IsActiveAt(end));
        });
    }

    [RewardTrackUtcDatabaseFact]
    public async Task ClaimStampsTheCapturedInstantAndRollsBackWhenTheWriteFails()
    {
        await WithSchema(async connectionString =>
        {
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                connection.Execute("CREATE TABLE badge_definitions (code VARCHAR(64) PRIMARY KEY, required_right VARCHAR(64) NOT NULL DEFAULT '') ENGINE=InnoDB");
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/15_IntroductionRewardTrack.sql")));
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/35_UseUtcRewardTrackTimes.sql")));
                connection.Execute("INSERT INTO reward_tracks (id, theme, sort_order, has_premium, enabled) VALUES ('open', 'blue', 4, 0, 1)");
                connection.Execute("INSERT INTO reward_track_prizes (track_id, id, required_points, product_item_type_id, reward_type, extra_params, reward_amount, premium, sort_order) VALUES ('open', 'coins', 10, 0, 'duckets', '', 3, 0, 1)");
                connection.Execute("INSERT INTO users_reward_tracks (user_id, track_id, points, premium) VALUES (7, 'open', 60, 0)");
            }

            var captured = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);
            var manager = new RewardTrackManager(NullLogger<RewardTrackManager>.Instance, new HabbiconDatabaseTests.TestDatabase(connectionString), NoBadges(), new FixedTimeProvider(captured));
            await manager.Start();
            var habbo = new Habbo { Id = 7, Username = "claimer" };
            var (client, sent) = HabbiconTestSupport.Client(habbo);

            // The users table is absent, so the duckets credit fails and the claim row must roll back with it.
            await manager.Claim(client, "open", "coins");

            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM users_reward_track_prizes WHERE prize_id = 'coins'"));
                connection.Execute("CREATE TABLE users (id INT PRIMARY KEY, credits INT NOT NULL DEFAULT 0, activity_points INT NOT NULL DEFAULT 0, vip_points INT NOT NULL DEFAULT 0)");
                connection.Execute("INSERT INTO users (id) VALUES (7)");
            }

            sent.Clear();
            await manager.Claim(client, "open", "coins");

            using var verify = new MySqlConnection(connectionString);
            verify.Open();
            Assert.Equal("2040-01-02 03:04:05.123456", verify.ExecuteScalar<string>("SELECT CAST(claimed_at AS CHAR) FROM users_reward_track_prizes WHERE prize_id = 'coins'"));
            Assert.Equal(3, verify.ExecuteScalar<int>("SELECT activity_points FROM users WHERE id = 7"));
            Assert.Equal(0, verify.ExecuteScalar<int>("SELECT credits FROM users WHERE id = 7"));
        });
    }

    [RewardTrackUtcDatabaseFact]
    public async Task PristineDefinitionsUseNullableUtcWindowsAndNullIntroductionBounds()
    {
        await WithSchema(async connectionString =>
        {
            var dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute(Statement(dump, "CREATE TABLE IF NOT EXISTS reward_tracks ("));
            connection.Execute(Statement(dump, "CREATE TABLE IF NOT EXISTS users_reward_track_prizes ("));
            connection.Execute(Statement(dump, "INSERT IGNORE INTO reward_tracks"));

            Assert.Equal("NULL", connection.ExecuteScalar<string>("SELECT IFNULL(CAST(starts_at AS CHAR), 'NULL') FROM reward_tracks WHERE id = 'introduction'"));
            Assert.Equal("NULL", connection.ExecuteScalar<string>("SELECT IFNULL(CAST(ends_at AS CHAR), 'NULL') FROM reward_tracks WHERE id = 'introduction'"));

            foreach (var (table, column) in new[] { ("reward_tracks", "starts_at"), ("reward_tracks", "ends_at"), ("users_reward_track_prizes", "claimed_at") })
            {
                var row = connection.QuerySingle<(string Type, string Nullable, string? Default, int Precision)>(
                    "SELECT DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, DATETIME_PRECISION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column",
                    new
                    {
                        table,
                        column
                    });
                Assert.Equal(("datetime", "YES", "NULL", 6), row);
            }
        });
    }

    private static string Statement(string dump, string opening)
    {
        var start = dump.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"Pristine dump has no statement starting {opening}.");
        }

        return dump[start..(dump.IndexOf(';', start) + 1)];
    }

    private static IReadOnlyList<RewardTrack> Loaded(RewardTrackManager manager) =>
        (List<RewardTrack>)typeof(RewardTrackManager).GetField("_tracks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

    private static IBadgeManager NoBadges() =>
        CatalogSnapshotTestSupport.Proxy<IBadgeManager>((method, _) => throw new NotSupportedException(method));

    private static async Task WithSchema(Func<string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable("PLUS_REWARD_TRACK_TEST_CONNECTION_STRING")!;
        var schema = "task_reward_track_tests_" + Guid.NewGuid().ToString("N")[..12];
        var options = new MySqlConnectionStringBuilder(server) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true };

        using (var admin = new MySqlConnection(server))
        {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{schema}`");
        }

        try
        {
            await body(options.ConnectionString);
        }
        finally
        {
            using var admin = new MySqlConnection(server);
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }
}

public sealed class RewardTrackUtcDatabaseFactAttribute : Xunit.FactAttribute
{
    public RewardTrackUtcDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_REWARD_TRACK_TEST_CONNECTION_STRING")))
        {
            Skip = "Set PLUS_REWARD_TRACK_TEST_CONNECTION_STRING to a server that can create and drop disposable task_reward_track_tests_ schemas.";
        }
    }
}
