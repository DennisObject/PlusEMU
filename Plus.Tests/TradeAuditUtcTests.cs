using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class TradeAuditUtcTests
{
    [RoomComponentDatabaseFact]
    public void MigrationAndRuntimeWritePreserveUnknownFractionalAndFutureInstants()
    {
        foreach (var mode in new[] { "", "STRICT_TRANS_TABLES" })
            InSchema((connection, database) =>
            {
                connection.Execute(PristineTable().Replace("`timestamp` datetime(6) DEFAULT NULL", "`timestamp` char(20) DEFAULT ''"));
                connection.Execute("SET SESSION sql_mode=@mode; SET time_zone='+05:30'", new { mode });
                connection.Execute("""
                    INSERT INTO logs_client_trade (id, `timestamp`) VALUES
                        (1,NULL), (2,''), (3,'0'), (4,'-1'), (5,'not-a-date'),
                        (6,'2200000000.123456'), (7,'253402300800'), (8,'253402300799.999999');
                    """);
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/42_UseUtcTradeAuditTimes.sql")));
                var rows = connection.Query<AuditTimeRow>("SELECT id AS Id, `timestamp` AS CreatedAt FROM logs_client_trade ORDER BY id").ToArray();
                Assert.All(rows.Where(row => row.Id <= 5 || row.Id == 7), row => Assert.Null(row.CreatedAt));
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).AddTicks(1_234_560), rows[5].CreatedAt);
                Assert.Equal(DateTimeOffset.MaxValue.AddTicks(-9), rows[7].CreatedAt);
                AssertMetadata(connection);
                var now = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.FromHours(-7)).AddTicks(6_543_210);
                var clock = new CountingClock(now);
                ((ITradeStore)new RoomTradingComponent(database, clock, TestRoomSettings.Empty)).Log(7, 8, "90;", "91;");
                var written = connection.QuerySingle<AuditTimeRow>("SELECT id AS Id, `timestamp` AS CreatedAt FROM logs_client_trade WHERE `1id`=7");
                Assert.Equal(now.ToUniversalTime(), written.CreatedAt);
                Assert.Equal(1, clock.Reads);
                Assert.Equal((7, 8, "90;", "91;"), connection.QuerySingle<(int, int, string, string)>(
                    "SELECT `1id`, `2id`, `1items`, `2items` FROM logs_client_trade WHERE `1id`=7"));
            });
    }

    [RoomComponentDatabaseFact]
    public void PristineTradeAuditDefinitionIsNativeWithoutMigration()
    {
        InSchema((connection, database) =>
        {
            connection.Execute(PristineTable());
            AssertMetadata(connection);
            var now = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);
            ((ITradeStore)new RoomTradingComponent(database, new CountingClock(now), TestRoomSettings.Empty)).Log(1, 2, "", "");
            Assert.Equal(now, connection.QuerySingle<AuditTimeRow>("SELECT id AS Id, `timestamp` AS CreatedAt FROM logs_client_trade").CreatedAt);
        });
    }

    private static void AssertMetadata(MySqlConnection connection)
    {
        Assert.Equal(("datetime", 6, "YES", 6), connection.QuerySingle<(string, int, string, int)>("""
            SELECT DATA_TYPE, DATETIME_PRECISION, IS_NULLABLE, ORDINAL_POSITION FROM information_schema.columns
            WHERE table_schema=DATABASE() AND table_name='logs_client_trade' AND column_name='timestamp'
            """));
        Assert.Equal("id", connection.QuerySingle<string>("""
            SELECT COLUMN_NAME FROM information_schema.statistics
            WHERE table_schema=DATABASE() AND table_name='logs_client_trade' AND index_name='PRIMARY'
            """));
    }

    private static string PristineTable() => Regex.Match(
        File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql")),
        @"CREATE TABLE `logs_client_trade` \(.*?;", RegexOptions.Singleline).Value;

    private static void InSchema(Action<MySqlConnection, IDatabase> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        { AllowZeroDateTime = true, ConvertZeroDateTime = true };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_trade_audit_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            run(connection, new ProbeDatabase(options.ConnectionString));
        }
        finally { admin.Execute($"DROP DATABASE `{schema}`"); }
    }

    private sealed class AuditTimeRow
    {
        public int Id { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }
    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
