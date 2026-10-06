using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Quests;
using Xunit;

namespace Plus.Tests;

public sealed class QuestDefinitionDatabaseTests
{
    [RoomComponentDatabaseFact]
    public async Task MigrationAndAsyncManagerLoadPreserveUtcDefinitionTimes()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "quest_definition_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE quests (
                    id INT UNSIGNED PRIMARY KEY, type VARCHAR(32) NOT NULL, level_num INT NOT NULL,
                    goal_type INT NOT NULL, goal_data INT UNSIGNED NOT NULL, action VARCHAR(32) NOT NULL,
                    pixel_reward INT NOT NULL, data_bit VARCHAR(2) NOT NULL, reward_type INT NOT NULL,
                    timestamp_unlock DECIMAL(20,6) NULL, timestamp_lock DECIMAL(20,6) NULL);
                INSERT INTO quests VALUES
                    (1, 'zero', 1, 9, 1, 'CHATWITHSOMEONE', 10, '', 3, 0, 0),
                    (2, 'null', 1, 9, 1, 'CHATWITHSOMEONE', 10, '', 3, NULL, -1),
                    (3, 'future', 1, 9, 1, 'CHATWITHSOMEONE', 10, '', 3,
                        1700000000.654321, 2200000000.123456);
                """);
            connection.Execute(File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", "Migrations",
                "28_UseUtcQuestDefinitionTimes.sql")));

            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
            { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true }.ConnectionString);
            var manager = new QuestManager(database, null!, TestLogging.For<QuestManager>(), null!);
            await manager.Start();

            Assert.Equal(20, manager.StartOrder);
            Assert.Null(manager.GetQuest(1)!.UnlocksAt);
            Assert.Null(manager.GetQuest(1)!.LocksAt);
            Assert.Null(manager.GetQuest(2)!.UnlocksAt);
            Assert.Null(manager.GetQuest(2)!.LocksAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(6_543_210),
                manager.GetQuest(3)!.UnlocksAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).AddTicks(1_234_560),
                manager.GetQuest(3)!.LocksAt);
            Assert.Equal(2, connection.QuerySingle<int>("""
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = DATABASE() AND table_name = 'quests'
                    AND column_name IN ('timestamp_unlock', 'timestamp_lock')
                    AND DATA_TYPE = 'datetime' AND DATETIME_PRECISION = 6
                """));
            var pristineSchema = File.ReadAllText(Path.Combine(RepositoryRoot(), "Resources", "SQLs",
                "Original Database.sql"));
            Assert.Contains("`timestamp_unlock` datetime(6) NULL DEFAULT NULL", pristineSchema);
            Assert.Contains("`timestamp_lock` datetime(6) NULL DEFAULT NULL", pristineSchema);
            Assert.Contains("'2012-12-03 06:00:00'", pristineSchema);
            Assert.DoesNotContain("'1354514400'", pristineSchema);
        }
        finally {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Plus Emulator.csproj"))) {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
