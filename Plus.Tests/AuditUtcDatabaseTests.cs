using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Xunit;

namespace Plus.Tests;

public class AuditUtcDatabaseTests
{
    private static readonly DateTimeOffset Captured = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);

    [AuditUtcDatabaseFact]
    public async Task CommandAndHousekeepingWritesRoundTripAfterMigration()
    {
        await WithSchema(async connectionString =>
        {
            CreateLegacyTables(connectionString);
            RunFile(connectionString, "Database/Migrations/36_UseUtcAuditLogTimes.sql");

            new CommandManager([], null!, new HabbiconDatabaseTests.TestDatabase(connectionString), new FixedTimeProvider(Captured)).LogCommand(7, ":test", "machine");
            var audit = new HousekeepingAuditLog(new HabbiconDatabaseTests.TestDatabase(connectionString), new FixedTimeProvider(Captured));
            audit.Write(7, "staff", "user.mute", HousekeepingOutcome.Success(HousekeepingTarget.User(8, "target"), "minutes=5"));
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                connection.Execute("INSERT INTO housekeeping_log (`timestamp`, actor_id, actor_name, action) VALUES (NULL, 7, 'staff', 'legacy.unknown')");
            }

            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                Assert.Equal("2040-01-02 03:04:05.123456", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM logs_client_staff WHERE data_string = ':test'"));
            }

            var rows = audit.List(10);
            Assert.Equal(new[] { "legacy.unknown", "user.mute" }, rows.Select(row => row.Action));
            Assert.Null(rows[0].CreatedAt);
            Assert.Equal(0, rows[0].LegacyTimestamp);
            Assert.Equal(Captured, rows[1].CreatedAt);
            Assert.Equal(int.MaxValue, rows[1].LegacyTimestamp);
        });
    }

    [AuditUtcDatabaseFact]
    public async Task DashboardSanctionCutoffUsesTheCapturedInstant()
    {
        await WithSchema(async connectionString =>
        {
            CreateLegacyTables(connectionString);
            RunFile(connectionString, "Database/Migrations/36_UseUtcAuditLogTimes.sql");
            var dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            var update = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/20_Housekeeping.sql"));
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                connection.Execute(Statement(update, "CREATE TABLE IF NOT EXISTS housekeeping_online_peaks ("));
                connection.Execute(Statement(dump, "CREATE TABLE `bans` ("));
                connection.Execute("INSERT INTO housekeeping_log (`timestamp`, actor_id, actor_name, action, success) VALUES " +
                    "('2040-01-02 03:04:05.000000', 7, 'staff', 'user.mute', 1), " +
                    "('2040-01-01 23:04:05.000000', 7, 'staff', 'user.mute', 1), " +
                    "('2040-01-01 03:04:05.123456', 7, 'staff', 'user.mute', 1), " +
                    "('2040-01-01 03:04:05.000000', 7, 'staff', 'user.mute', 1), " +
                    "('2040-01-02 02:04:05.000000', 7, 'staff', 'user.trade_lock', 1), " +
                    "('2040-01-02 02:04:05.000000', 7, 'staff', 'user.mute', 0), " +
                    "('2040-01-02 02:04:05.000000', 7, 'staff', 'user.ban', 1)");
                connection.Execute("INSERT INTO bans (bantype, value, reason, expire, added_by, added_date) VALUES " +
                    "('user', 'a', 'x', '2050-01-01 00:00:00', 'staff', '2040-01-02 02:04:05.000000'), " +
                    "('user', 'b', 'x', '2050-01-01 00:00:00', 'staff', '2039-12-31 03:04:05.000000'), " +
                    "('user', 'c', 'x', '2050-01-01 00:00:00', 'staff', NULL)");
            }
            var database = new HabbiconDatabaseTests.TestDatabase(connectionString);
            new HousekeepingAuditLog(database, new FixedTimeProvider(Captured)).Write(7, "staff", "user.trade_lock", HousekeepingOutcome.Success(HousekeepingTarget.User(8, "target"), "hours=1"));
            var clock = new FixedTimeProvider(Captured);
            var lookups = new HousekeepingLookups(Clients(), null!, Moderation(), Rooms(), database, clock, new ServerUptime(clock));

            // Counted: the mute 1s before the instant, the mute at -4h, the trade lock at -1h, the write at the instant, and the ban at -1h.
            // Excluded: the row exactly at -24h (the cutoff is exclusive), older rows, failed rows, other actions, and the 2039 and NULL bans.
            Assert.Equal(5, lookups.Dashboard().SanctionsLast24h);
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

    private static string Statement(string text, string opening)
    {
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException($"Dump has no statement starting {opening}.");
        return text[start..(text.IndexOf(';', start) + 1)];
    }

    private static IGameClientManager Clients() =>
        CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, _) => method switch
        {
            "get_Count" => 0,
            _ => throw new NotSupportedException(method),
        });

    private static IModerationManager Moderation() =>
        CatalogSnapshotTestSupport.Proxy<IModerationManager>((method, _) => method switch
        {
            "get_GetTickets" => new List<ModerationTicket>(),
            _ => throw new NotSupportedException(method),
        });

    private static IRoomManager Rooms() =>
        CatalogSnapshotTestSupport.Proxy<IRoomManager>((method, _) => method switch
        {
            "GetRooms" => new List<Room>(),
            _ => throw new NotSupportedException(method),
        });

    private static async Task WithSchema(Func<string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable("PLUS_AUDIT_UTC_TEST_CONNECTION_STRING")!;
        var schema = "task_audit_utc_tests_" + Guid.NewGuid().ToString("N")[..12];
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

public sealed class AuditUtcDatabaseFactAttribute : Xunit.FactAttribute
{
    public AuditUtcDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_AUDIT_UTC_TEST_CONNECTION_STRING")))
            Skip = "Set PLUS_AUDIT_UTC_TEST_CONNECTION_STRING to a server that can create and drop disposable task_audit_utc_tests_ schemas.";
    }
}
