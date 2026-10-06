using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class ModerationBanUtcTests
{
    private static readonly DateTimeOffset Now = new(2039, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void ExpiryIsInactiveAtTheExactBoundaryAndUnknownIsInactive()
    {
        var expiresAt = Now.AddTicks(1);
        var ban = new ModerationBan(ModerationBanType.Username, "user", "reason", expiresAt);

        Assert.False(ban.IsExpiredAt(Now));
        Assert.True(ban.IsExpiredAt(expiresAt));
        Assert.True(ban.IsExpiredAt(expiresAt.AddTicks(1)));
        Assert.True(new ModerationBan(ModerationBanType.Username, "user", "reason", null).IsExpiredAt(Now));
    }

    [RoomComponentDatabaseFact]
    public async Task MigrationLookupAndWritePreserveNullableFractionalPost2038Utc()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "moderation_bans_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true,
                AllowUserVariables = true
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE bans (
                    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    bantype ENUM('user','ip','machine') NOT NULL DEFAULT 'user', value VARCHAR(50) NOT NULL,
                    reason TEXT NOT NULL, expire DOUBLE NULL, added_by VARCHAR(50) NOT NULL, added_date VARCHAR(50) NULL);
                INSERT INTO bans (bantype,value,reason,expire,added_by,added_date) VALUES
                    ('user','future','future',2200000000.123456,'probe','2200000001.654321'),
                    ('user','zero','zero',0,'probe','0'),
                    ('user','unknown','unknown',NULL,'probe',NULL),
                    ('user','exact','exact',2177550245,'probe','2177550245'),
                    ('user','invalid-added','invalid',2200000000,'probe','not-a-date');
                """);
            connection.Execute(File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", "Migrations", "34_UseUtcModerationBanTimes.sql")));
            Assert.Equal(2, connection.QuerySingle<int>("""
                SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'bans'
                    AND column_name IN ('expire','added_date') AND data_type = 'datetime' AND datetime_precision = 6 AND is_nullable = 'YES'
                """));
            Assert.Null(connection.QuerySingleOrDefault<DateTime?>("SELECT expire FROM bans WHERE value='zero'"));
            Assert.Null(connection.QuerySingleOrDefault<DateTime?>("SELECT added_date FROM bans WHERE value='invalid-added'"));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_200_000_001).AddTicks(6_543_210).UtcDateTime,
                DateTime.SpecifyKind(connection.QuerySingle<DateTime>("SELECT added_date FROM bans WHERE value='future'"), DateTimeKind.Utc));

            SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
            var database = new ProbeDatabase(connectionString);
            var expectedExpiry = DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).AddTicks(1_234_560);
            var lookup = new BanLookup(database, new FixedClock(Now));
            Assert.Equal(expectedExpiry, (await lookup.Find("future", "192.0.2.1"))?.ExpiresAt);
            Assert.Null(await lookup.Find("zero", "192.0.2.1"));
            Assert.Null(await lookup.Find("unknown", "192.0.2.1"));

            var loadClock = new CountingClock(Now, TimeZoneInfo.Utc);
            var loadedManager = Manager(database, loadClock);
            loadedManager.ReCacheBans();
            Assert.True(loadedManager.IsBanned("future", out var loaded));
            Assert.Equal(expectedExpiry, loaded.ExpiresAt);
            Assert.False(loadedManager.IsBanned("exact", out _));

            var clock = new CountingClock(Now, TimeZoneInfo.CreateCustomTimeZone("plus-nine", TimeSpan.FromHours(9), "test", "test"));
            var manager = Manager(database, clock);
            var writtenExpiry = Now.AddYears(2).AddTicks(6_543_210);
            await manager.BanUser("probe", ModerationBanType.Machine, "machine-future", "reason", writtenExpiry);
            Assert.Equal(1, clock.Calls);
            Assert.Equal(writtenExpiry.UtcDateTime, connection.QuerySingle<DateTime>("SELECT expire FROM bans WHERE value='machine-future'"));
            Assert.Equal(Now.UtcDateTime, connection.QuerySingle<DateTime>("SELECT added_date FROM bans WHERE value='machine-future'"));
        }
        finally
        {
            server.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static ModerationManager Manager(IDatabase database, TimeProvider clock) => new(database,
        NullLogger<ModerationManager>.Instance, DispatchProxy.Create<ISessionIssuer, EmptyProxy>(),
        new HousekeepingActionTests.FakeClients(), new AccountSessionGate(), clock);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Plus Emulator.csproj")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public int Calls
        {
            get; private set;
        }
        public override TimeZoneInfo LocalTimeZone => zone;
        public override DateTimeOffset GetUtcNow()
        {
            Calls++;

            return now;
        }
    }

    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
