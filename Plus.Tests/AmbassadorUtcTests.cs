using System.Data;
using System.Reflection;
using Plus.Communication.Packets.Incoming.Rooms.Action;
using Plus.HabboHotel.GameClients;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Ambassadors;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class AmbassadorUtcTests
{
    [Fact]
    public async Task UnauthorizedWarningDoesNotReadClockPersistOrPublish()
    {
        var ambassador = new Habbo { Id = 7 };
        var (client, sent) = HabbiconTestSupport.Client(ambassador);
        ambassador.Client = client;
        var clock = new CountingClock(DateTimeOffset.UnixEpoch);
        var manager = new AmbassadorsManager(new ThrowingDatabase(), clock, Clients(_ => throw new InvalidOperationException("unexpected target lookup")));
        await manager.Warn(client, 8, "warning");
        Assert.Equal(0, clock.Reads);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task PersistenceFailureDoesNotPublishWarningPackets()
    {
        var ambassador = AuthorizedAmbassador();
        var (client, sent) = HabbiconTestSupport.Client(ambassador);
        ambassador.Client = client;
        var clock = new CountingClock(DateTimeOffset.UnixEpoch);
        var (target, _) = HabbiconTestSupport.Client(new Habbo { Id = 8 });
        var manager = new AmbassadorsManager(new ThrowingDatabase(), clock, Clients(_ => target));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.Warn(client, 8, "warning"));
        Assert.Empty(sent);
    }

    [RoomComponentDatabaseFact]
    public async Task LegacyMigrationAndRuntimeWritePreserveFutureMicrosecondsAndUtc()
    {
        var connectionString = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_ambassadors_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(connectionString);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(connectionString) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true }.ConnectionString);
            using (var connection = database.Connection())
            {
                connection.Execute("CREATE TABLE ambassador_logs(id INT AUTO_INCREMENT PRIMARY KEY,user_id INT,target VARCHAR(50),sanctions_type TEXT,`timestamp` DECIMAL(20,6) NULL)");
                connection.Execute("INSERT INTO ambassador_logs(`timestamp`) VALUES(NULL),(0),(-1),(2200000000.123456)");
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/29_UseUtcAmbassadorLogTimes.sql")));
                Assert.Equal(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ambassador_logs WHERE `timestamp` IS NULL"));
                Assert.Equal("2039-09-18 23:06:40.123456", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM ambassador_logs WHERE id=4"));
                Assert.Equal("datetime", connection.ExecuteScalar<string>("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ambassador_logs' AND COLUMN_NAME='timestamp'"));
                Assert.Equal(6, connection.ExecuteScalar<int>("SELECT DATETIME_PRECISION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ambassador_logs' AND COLUMN_NAME='timestamp'"));
            }
            var now = new DateTimeOffset(2040, 12, 31, 20, 0, 0, TimeSpan.FromHours(2)).AddTicks(1234560);
            var clock = new CountingClock(now);
            var ambassador = AuthorizedAmbassador();
            var (client, actorPackets) = HabbiconTestSupport.Client(ambassador);
            ambassador.Client = client;
            var target = new Habbo { Id = 8, Username = "target" };
            var (targetClient, targetPackets) = HabbiconTestSupport.Client(target);
            target.Client = targetClient;
            await new AmbassadorsManager(database, clock, Clients(id => id == 8 ? targetClient : null)).Warn(client, 8, "warning");
            using var verify = database.Connection();
            Assert.Equal(now.ToUniversalTime(), verify.ExecuteScalar<DateTimeOffset>("SELECT `timestamp` FROM ambassador_logs WHERE user_id=7"));
            Assert.Equal(1, clock.Reads);
            Assert.Empty(actorPackets); // An actor outside a room receives no whisper, as before.
            Assert.Single(targetPackets);
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [Fact]
    public async Task AlertHandlerDecodesOnlyTheIdAndDelegates()
    {
        var manager = new RecordingAmbassadors();
        await new AmbassadorAlertEvent(manager).Parse(null!, HabbiconTestSupport.Incoming(8));
        Assert.Equal((8, "Alert"), Assert.Single(manager.Calls));
        manager.Calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new AmbassadorAlertEvent(manager).Parse(null!, HabbiconTestSupport.Incoming()));
        Assert.Empty(manager.Calls);
    }

    [Fact]
    public async Task MissingOrDisconnectedTargetsDoNotPersistSampleTimeOrPublish()
    {
        var (client, sent) = HabbiconTestSupport.Client(AuthorizedAmbassador());
        var clock = new CountingClock(DateTimeOffset.UnixEpoch);
        var manager = new AmbassadorsManager(new ThrowingDatabase(), clock, Clients(_ => null));
        await manager.Warn(client, 8, "Alert");
        Assert.Equal(0, clock.Reads);
        Assert.Empty(sent);
    }

    private static IGameClientManager Clients(Func<int, GameClient?> lookup)
    {
        var clients = DispatchProxy.Create<IGameClientManager, ClientProxy>();
        ((ClientProxy)(object)clients).Lookup = lookup;
        return clients;
    }
    public class ClientProxy : DispatchProxy
    {
        public Func<int, GameClient?> Lookup = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "GetClientByUserId" ? Lookup((int)args![0]!) : throw new NotSupportedException(method.Name);
    }
    private sealed class RecordingAmbassadors : IAmbassadorsManager
    {
        public List<(int UserId, string Message)> Calls = [];
        public Task Warn(GameClient session, int targetId, string message)
        {
            Calls.Add((targetId, message));
            return Task.CompletedTask;
        }
    }

    private static Habbo AuthorizedAmbassador() => new()
    {
        Id = 7,
        Access = UserAccess.Create([], [new(PermissionKeys.Ambassador, false)])
    };
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
    private sealed class ThrowingDatabase : IDatabase
    {
        public IDbConnection Connection() => throw new InvalidOperationException("Persistence unavailable");
        public bool IsConnected() => false;
    }
}
