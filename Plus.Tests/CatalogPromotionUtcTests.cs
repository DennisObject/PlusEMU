using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Catalog;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogPromotionUtcTests
{
    [Theory]
    [InlineData(-1, false, 0)]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 0)]
    public void ExpiryUsesTheExactInstantAcrossOffsets(int ticksAfterExpiry, bool expired, int seconds)
    {
        var expiry = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)).AddTicks(1_234_560);
        var promotion = new CatalogPromotion { ExpiresAt = expiry };
        var now = expiry.ToOffset(TimeSpan.FromHours(-7)).AddTicks(ticksAfterExpiry);
        Assert.Equal(TimeSpan.Zero, promotion.ExpiresAt!.Value.Offset);
        Assert.Equal(expired, promotion.HasExpiredAt(now));
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) =>
            method == "get_Promotions" ? new[] { promotion } : throw new InvalidOperationException(method));
        var page = new CatalogSnapshotService(catalog, new FixedTimeProvider(now)).CapturePage(new() { Layout = "frontpage" }, -1);

        if (expired) {
            Assert.Empty(page.Promotions);
        }
        else {
            Assert.Equal(seconds, Assert.Single(page.Promotions).SecondsLeft);
        }
    }

    [Fact]
    public void UnknownExpiryIsUnboundedAndHugeCountdownClampsAtCapture()
    {
        var unknown = new CatalogPromotion();
        Assert.False(unknown.HasExpiredAt(DateTimeOffset.MaxValue));
        Assert.Equal(TimeSpan.Zero, unknown.RemainingAt(DateTimeOffset.MaxValue));
        var promotion = new CatalogPromotion { ExpiresAt = DateTimeOffset.MaxValue };
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) =>
            method == "get_Promotions" ? new[] { promotion } : throw new InvalidOperationException(method));
        var snapshot = new CatalogSnapshotService(catalog, new FixedTimeProvider(DateTimeOffset.UnixEpoch))
            .CapturePage(new() { Layout = "frontpage" }, -1);
        promotion.ExpiresAt = DateTimeOffset.UnixEpoch;
        Assert.Equal(int.MaxValue, Assert.Single(snapshot.Promotions).SecondsLeft);
    }

    [Fact]
    public void CountdownTruncatesTicksBeforeTheIntegerLimit()
    {
        var promotion = new CatalogPromotion
        {
            ExpiresAt = DateTimeOffset.UnixEpoch.AddTicks((long)int.MaxValue * TimeSpan.TicksPerSecond - 1)
        };
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) =>
            method == "get_Promotions" ? new[] { promotion } : throw new InvalidOperationException(method));
        var snapshot = new CatalogSnapshotService(catalog, new FixedTimeProvider(DateTimeOffset.UnixEpoch))
            .CapturePage(new() { Layout = "frontpage" }, -1);
        Assert.Equal(int.MaxValue - 1, Assert.Single(snapshot.Promotions).SecondsLeft);
    }

    [RoomComponentDatabaseFact]
    public async Task MigrationAndAwaitedCatalogLoadMaterializeUnknownFractionalAndFutureExpiries()
    {
        await InSchema(async (connection, database) =>
        {
            CreateCatalogTables(connection);
            connection.Execute(Read("Resources/SQLs/Updates/17_OfficialCatalogStructure.sql"));
            connection.Execute("""
                ALTER TABLE catalog_promotions MODIFY expires_at DECIMAL(20,6) NULL;
                INSERT INTO catalog_promotions (id, position, expires_at) VALUES
                    (1,1,0), (2,2,-1), (3,3,NULL), (4,4,2200000000.123456), (5,5,253402300800), (6,6,253402300799.999999);
                SET time_zone='+05:30';
                """);
            connection.Execute(Read("Database/Migrations/41_UseUtcCatalogPromotionTimes.sql"));
            var manager = Manager(database);
            await manager.Start();
            var rows = manager.Promotions.OrderBy(value => value.Id).ToArray();
            Assert.Equal(6, rows.Length);
            Assert.All(rows.Where(row => row.Id is not 4 and not 6), row => Assert.Null(row.ExpiresAt));
            var expiry = DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).AddTicks(1_234_560);
            Assert.Equal(expiry, rows[3].ExpiresAt);
            Assert.Equal(DateTimeOffset.MaxValue.AddTicks(-9), rows[5].ExpiresAt);
            Assert.False(rows[3].HasExpiredAt(expiry.AddTicks(-1)));
            Assert.True(rows[3].HasExpiredAt(expiry));
            Assert.True(rows[3].HasExpiredAt(expiry.AddTicks(1)));
            AssertMetadata(connection);
            var runtime = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.FromHours(-7)).AddTicks(6_543_210);
            connection.Execute("UPDATE catalog_promotions SET expires_at=@expiresAt WHERE id=4", new { expiresAt = runtime.UtcDateTime });
            await manager.Start();
            Assert.Equal(runtime.ToUniversalTime(), manager.Promotions.Single(row => row.Id == 4).ExpiresAt);
        });
    }

    [RoomComponentDatabaseFact]
    public async Task PristinePromotionDefinitionIsNativeWithoutApplyingTheMigration()
    {
        await InSchema(async (connection, database) =>
        {
            CreateCatalogTables(connection);
            var pristine = Read("Resources/SQLs/Original Database.sql");
            var statement = Regex.Match(pristine, @"ALTER TABLE catalog_promotions\s.*?;", RegexOptions.Singleline);
            Assert.True(statement.Success);
            connection.Execute(statement.Value);
            AssertMetadata(connection);
            var expiry = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);
            connection.Execute("INSERT INTO catalog_promotions (id, expires_at) VALUES (1,@expiresAt),(2,NULL)", new { expiresAt = expiry.UtcDateTime });
            var manager = Manager(database);
            await manager.Start();
            Assert.Equal(expiry, manager.Promotions.Single(row => row.Id == 1).ExpiresAt);
            Assert.Null(manager.Promotions.Single(row => row.Id == 2).ExpiresAt);
        });
    }

    private static void CreateCatalogTables(MySqlConnection connection)
    {
        var pristine = Read("Resources/SQLs/Original Database.sql");

        foreach (var table in new[] { "catalog_items", "catalog_deals", "catalog_pages", "catalog_bot_presets", "catalog_promotions" }) {
            var statement = Regex.Match(pristine, $@"CREATE TABLE `{table}` \(.*?;", RegexOptions.Singleline);
            Assert.True(statement.Success);
            connection.Execute(statement.Value);
        }

        connection.Execute("""
            ALTER TABLE catalog_pages ADD COLUMN required_club_level INT NOT NULL DEFAULT 0;
            ALTER TABLE catalog_items ADD COLUMN habbicon_id INT NOT NULL DEFAULT 0;
            CREATE TABLE catalog_club_offers (
                id INT PRIMARY KEY, name VARCHAR(64), days INT, credits INT, points INT, points_type INT,
                giftable BOOL, enabled BOOL);
            """);
        // These are the unchanged prerequisites of CatalogManager.Start; no catalog items are needed.
        var itemColumns = Regex.Match(Read("Resources/SQLs/Updates/17_OfficialCatalogStructure.sql"),
            @"ALTER TABLE catalog_items\s.*?;", RegexOptions.Singleline);
        connection.Execute(itemColumns.Value);
    }

    private static void AssertMetadata(MySqlConnection connection)
    {
        Assert.Equal(("datetime", 6, "YES"), connection.QuerySingle<(string Type, int Precision, string Nullable)>("""
            SELECT DATA_TYPE, DATETIME_PRECISION, IS_NULLABLE FROM information_schema.columns
            WHERE table_schema=DATABASE() AND table_name='catalog_promotions' AND column_name='expires_at'
            """));
        Assert.Equal(11, connection.QuerySingle<int>("SELECT ORDINAL_POSITION FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='catalog_promotions' AND column_name='expires_at'"));
        Assert.Equal("id", connection.QuerySingle<string>("""
            SELECT COLUMN_NAME FROM information_schema.statistics
            WHERE table_schema=DATABASE() AND table_name='catalog_promotions' AND index_name='PRIMARY'
            """));
    }

    private static CatalogManager Manager(IDatabase database) =>
        new(null!, null!, null!, null!, database, TestLogging.For<CatalogManager>(), null!);

    private static async Task InSchema(Func<MySqlConnection, IDatabase, Task> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        { AllowZeroDateTime = true, ConvertZeroDateTime = true };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_catalog_promotion_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            await run(connection, new ProbeDatabase(options.ConnectionString));
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static string Read(string path) => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../", path)));
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
