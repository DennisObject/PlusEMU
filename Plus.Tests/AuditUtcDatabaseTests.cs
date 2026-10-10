using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Xunit;

namespace Plus.Tests;

public class AuditUtcDatabaseTests
{
    private static readonly DateTimeOffset Captured = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);

    [AuditUtcDatabaseFact]
    public async Task CommandLogWritesRoundTripAfterMigration()
    {
        await WithSchema(async connectionString =>
        {
            CreateLegacyTables(connectionString);
            RunFile(connectionString, "Database/Migrations/36_UseUtcAuditLogTimes.sql");

            new CommandManager([], null!, new HabbiconDatabaseTests.TestDatabase(connectionString), new FixedTimeProvider(Captured)).LogCommand(7, ":test", "machine");

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal("2040-01-02 03:04:05.123456", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM logs_client_staff WHERE data_string = ':test'"));
        });
    }

    [AuditUtcDatabaseFact]
    public async Task MigrationConvertsLegacySecondsAndKeepsTheCompoundIndex()
    {
        await WithSchema(async connectionString =>
        {
            using (var connection = new MySqlConnection(connectionString)) {
                connection.Open();
                connection.Execute("CREATE TABLE logs_client_staff (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL DEFAULT '0', data_string TEXT NOT NULL, machine_id VARCHAR(75) NOT NULL DEFAULT '', `timestamp` DOUBLE NULL DEFAULT '0') ENGINE=InnoDB");
                connection.Execute("CREATE TABLE housekeeping_log (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, `timestamp` INT NULL, actor_id INT NOT NULL, actor_name VARCHAR(125) NOT NULL DEFAULT '', target_type VARCHAR(16) NOT NULL DEFAULT 'user', target_id INT NOT NULL DEFAULT 0, target_label VARCHAR(255) NOT NULL DEFAULT '', action VARCHAR(64) NOT NULL, detail VARCHAR(500) NOT NULL DEFAULT '', success TINYINT(1) NOT NULL DEFAULT 1, KEY timestamp_action (`timestamp`, action), KEY actor (actor_id)) ENGINE=InnoDB");
                connection.Execute("INSERT INTO logs_client_staff (user_id, data_string, machine_id, `timestamp`) VALUES (1, 'fraction', '', 1700000000.25), (1, 'future', '', 2500000000.5), (1, 'zero', '', 0), (1, 'negative', '', -3), (1, 'null', '', NULL)");
                connection.Execute("INSERT INTO housekeeping_log (`timestamp`, actor_id, action) VALUES (2147483647, 1, 'max'), (0, 1, 'zero'), (-1, 1, 'negative'), (NULL, 1, 'null')");
            }

            RunFile(connectionString, "Database/Migrations/36_UseUtcAuditLogTimes.sql");

            using var verify = new MySqlConnection(connectionString);
            verify.Open();
            Assert.Equal(new (string, string?)[]
            {
                ("fraction", "2023-11-14 22:13:20.250000"),
                ("future", "2049-03-22 04:26:40.500000"),
                ("zero", null),
                ("negative", null),
                ("null", null),
            }, verify.Query<(string Data, string? Stamp)>("SELECT data_string AS Data, CAST(`timestamp` AS CHAR) AS Stamp FROM logs_client_staff ORDER BY id").ToList());
            Assert.Equal(new (string, string?)[]
            {
                ("max", "2038-01-19 03:14:07.000000"),
                ("zero", null),
                ("negative", null),
                ("null", null),
            }, verify.Query<(string Action, string? Stamp)>("SELECT action AS Action, CAST(`timestamp` AS CHAR) AS Stamp FROM housekeeping_log ORDER BY id").ToList());

            foreach (var table in new[] { "logs_client_staff", "housekeeping_log" }) {
                Assert.Equal(("datetime", "YES", "NULL", 6), verify.QuerySingle<(string, string, string?, int)>(
                    "SELECT DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, DATETIME_PRECISION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = 'timestamp'",
                    new { table }));
            }

            Assert.Equal(5, verify.ExecuteScalar<int>("SELECT ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'logs_client_staff' AND COLUMN_NAME = 'timestamp'"));
            Assert.Equal(2, verify.ExecuteScalar<int>("SELECT ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'housekeeping_log' AND COLUMN_NAME = 'timestamp'"));
            Assert.Equal(new[] { ("timestamp", 1), ("action", 2) }, verify.Query<(string ColumnName, int Seq)>(
                "SELECT COLUMN_NAME AS ColumnName, SEQ_IN_INDEX AS Seq FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'housekeeping_log' AND INDEX_NAME = 'timestamp_action' ORDER BY SEQ_IN_INDEX")
                .Select(row => (row.ColumnName, row.Seq)).ToArray());
        });
    }

    private static void CreateLegacyTables(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute("CREATE TABLE logs_client_staff (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL DEFAULT '0', data_string TEXT NOT NULL, machine_id VARCHAR(75) NOT NULL DEFAULT '', `timestamp` DOUBLE NOT NULL DEFAULT '0') ENGINE=InnoDB");
        connection.Execute("CREATE TABLE housekeeping_log (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, `timestamp` INT NOT NULL, actor_id INT NOT NULL, actor_name VARCHAR(125) NOT NULL DEFAULT '', target_type VARCHAR(16) NOT NULL DEFAULT 'user', target_id INT NOT NULL DEFAULT 0, target_label VARCHAR(255) NOT NULL DEFAULT '', action VARCHAR(64) NOT NULL, detail VARCHAR(500) NOT NULL DEFAULT '', success TINYINT(1) NOT NULL DEFAULT 1, KEY timestamp_action (`timestamp`, action), KEY actor (actor_id)) ENGINE=InnoDB");
        connection.Execute("CREATE TABLE users (id INT PRIMARY KEY, username VARCHAR(32) NOT NULL DEFAULT '') ENGINE=InnoDB");
        connection.Execute("CREATE TABLE rooms (id INT PRIMARY KEY) ENGINE=InnoDB");
    }

    private static void RunFile(string connectionString, string path)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo(path)));
    }

    private static async Task WithSchema(Func<string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable("PLUS_AUDIT_UTC_TEST_CONNECTION_STRING")!;
        var schema = "task_audit_utc_tests_" + Guid.NewGuid().ToString("N")[..12];
        var options = new MySqlConnectionStringBuilder(server) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true };

        using (var admin = new MySqlConnection(server)) {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{schema}`");
        }

        try {
            await body(options.ConnectionString);
        }
        finally {
            using var admin = new MySqlConnection(server);
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }
}

public sealed class AuditUtcDatabaseFactAttribute : Xunit.FactAttribute
{
    public AuditUtcDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_AUDIT_UTC_TEST_CONNECTION_STRING"))) {
            Skip = "Set PLUS_AUDIT_UTC_TEST_CONNECTION_STRING to a server that can create and drop disposable task_audit_utc_tests_ schemas.";
        }
    }
}
