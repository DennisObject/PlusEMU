using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Rooms.Action;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Ignores;
using Xunit;

namespace Plus.Tests;

public sealed class PlayerIgnoreServiceTests
{
    [Fact]
    public async Task HandlersDecodeAndAwaitOnlyTheService()
    {
        var service = new RecordingService();
        await new IgnoreUserEvent(service).Parse(null!, HabbiconTestSupport.Incoming("Alice"));
        await new UnignoreUserEvent(service).Parse(null!, HabbiconTestSupport.Incoming("Bob"));
        Assert.Equal(new[] { "ignore Alice", "unignore Bob" }, service.Calls);
        service.Calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new IgnoreUserEvent(service).Parse(null!, HabbiconTestSupport.Incoming()));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task IgnoreAndUnignoreCommitBeforeStatePacketsAndAchievement()
    {
        var f = new Fixture();
        f.Store.BeforeWrite = (_, target, ignored) =>
        {
            Assert.Equal(2, target);
            Assert.Equal(!ignored, f.Actor.IgnoresComponent.IsIgnored(2));
            Assert.Empty(f.Sent);
            Assert.Equal(ignored ? 0 : 1, f.Achievements);
            return Task.CompletedTask;
        };
        await f.Service.Ignore(f.Client, "target");
        Assert.True(f.Actor.IgnoresComponent.IsIgnored(2));
        Assert.Equal(1, f.Achievements);
        AssertStatus(Assert.Single(f.Sent), IgnoreStatus.Added);
        f.Sent.Clear();
        await f.Service.Unignore(f.Client, "target");
        Assert.False(f.Actor.IgnoresComponent.IsIgnored(2));
        Assert.Equal(1, f.Achievements);
        AssertStatus(Assert.Single(f.Sent), IgnoreStatus.Removed);
        Assert.Equal(new[] { true, false }, f.Store.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedPersistenceLeavesStatePacketsAndAchievementsUnchanged(bool ignore)
    {
        var f = new Fixture();
        if (!ignore) f.Actor.IgnoresComponent.PublishIgnore(2);
        f.Store.BeforeWrite = (_, _, _) => throw new InvalidOperationException("forced store failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ignore ? f.Service.Ignore(f.Client, "target") : f.Service.Unignore(f.Client, "target"));
        Assert.Equal(!ignore, f.Actor.IgnoresComponent.IsIgnored(2));
        Assert.Empty(f.Sent);
        Assert.Equal(0, f.Achievements);
    }

    [Fact]
    public async Task DeniedMissingAndDuplicateRequestsNeverWriteOrPublish()
    {
        var f = new Fixture();
        var room = f.Actor.CurrentRoom;
        f.Actor.CurrentRoom = null;
        await f.Service.Ignore(f.Client, "target");
        Assert.Equal(0, f.Lookups);
        f.Actor.CurrentRoom = room;
        await f.Service.Ignore(f.Client, "missing");
        f.Target.Access = EditorTestSupport.Access([PermissionKeys.ModerationTool]);
        await f.Service.Ignore(f.Client, "target");
        f.Target.Access = UserAccess.Empty;
        await f.Service.Unignore(f.Client, "target");
        f.Actor.IgnoresComponent.PublishIgnore(2);
        await f.Service.Ignore(f.Client, "target");
        Assert.Empty(f.Store.Writes);
        Assert.Empty(f.Sent);
        Assert.Equal(0, f.Achievements);
        f.Store.Refused = true;
        await f.Service.Unignore(f.Client, "target");
        Assert.True(f.Actor.IgnoresComponent.IsIgnored(2));
        Assert.Empty(f.Sent);
    }

    [Fact]
    public async Task AccountGateKeepsOverlappingUnignoreBehindCommittedIgnorePublication()
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.BeforeWrite = async (_, _, ignored) =>
        {
            if (ignored)
            {
                entered.SetResult();
                await release.Task;
            }
            else
                Assert.True(f.Actor.IgnoresComponent.IsIgnored(2));
        };
        var first = f.Service.Ignore(f.Client, "target");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = f.Service.Unignore(f.Client, "target");
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(f.Actor.IgnoresComponent.IsIgnored(2));
            Assert.Empty(f.Sent);
            Assert.Equal(new[] { true }, f.Store.Writes);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second);
        }
        Assert.False(f.Actor.IgnoresComponent.IsIgnored(2));
        Assert.Equal(new[] { true, false }, f.Store.Writes);
        Assert.Equal(2, f.Sent.Count);
    }

    [RoomComponentDatabaseFact]
    public async Task ProductionStoreSerializesDuplicatesRollsBackFailuresAndReloadsTargetIds()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_refactor_tests_ignore_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var connectionString = new MySqlConnectionStringBuilder(root)
            { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true }.ConnectionString;
            var database = new ProbeDatabase(connectionString);
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("CREATE TABLE users(id INT PRIMARY KEY); INSERT INTO users VALUES(7); CREATE TABLE user_ignores(user_id INT UNSIGNED NOT NULL,ignore_id INT UNSIGNED NOT NULL,PRIMARY KEY(user_id,ignore_id))");
            var store = new PlayerIgnoreStore(database);
            Assert.False(await store.SetIgnored(99, 2, true));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.SetIgnored(7, 2, true)));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_ignores"));
            var habbo = new Habbo { Id = 7 };
            await new IgnoresUserDataLoadingTask(database).Load(habbo);
            Assert.Equal(new[] { 2 }, habbo.IgnoresComponent.IgnoredUsers);
            connection.Execute("CREATE TRIGGER refuse_ignore BEFORE INSERT ON user_ignores FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced ignore failure'");
            await Assert.ThrowsAsync<MySqlException>(() => store.SetIgnored(7, 3, true));
            Assert.Equal(new[] { 2 }, connection.Query<int>("SELECT ignore_id FROM user_ignores").ToArray());
            Assert.True(await store.SetIgnored(7, 2, false));
            Assert.Empty(connection.Query<int>("SELECT ignore_id FROM user_ignores"));
            await new IgnoresUserDataLoadingTask(database).Load(habbo);
            Assert.Empty(habbo.IgnoresComponent.IgnoredUsers);
        }
        finally { server.Execute($"DROP DATABASE `{schema}`"); }
    }

    private static void AssertStatus((uint Header, byte[] Payload) sent, IgnoreStatus status)
    {
        Assert.Equal(ServerPacketHeader.IgnoreStatusComposer, sent.Header);
        var packet = new FlashIncomingPacket { Buffer = sent.Payload };
        Assert.Equal((int)status, packet.ReadInt());
        Assert.Equal("prepared name", packet.ReadString());
        Assert.False(packet.HasDataRemaining());
    }

    private sealed class Fixture
    {
        public Habbo Actor = new() { Id = 1, CurrentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)), IgnoresComponent = new([]) };
        public Habbo Target = new() { Id = 2, Username = "target", Access = UserAccess.Empty };
        public FlashGameClient Client;
        public List<(uint Header, byte[] Payload)> Sent;
        public RecordingStore Store = new();
        public PlayerIgnoreService Service;
        public int Achievements;
        public int Lookups;

        public Fixture()
        {
            (Client, Sent) = HabbiconTestSupport.Client(Actor);
            Client.SendCallback = args =>
            {
                var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
                Sent.Add((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));
                return true;
            };
            var (target, _) = HabbiconTestSupport.Client(Target);
            var clients = Proxy<IGameClientManager>((method, args) => method switch
            {
                "GetClientByUsername" => Lookup((string)args[0]!, target),
                "GetNameById" => Task.FromResult("prepared name"),
                _ => throw new NotSupportedException(method)
            });
            var achievements = Proxy<IAchievementManager>((method, _) =>
            {
                Assert.Equal("ProgressAchievement", method);
                Assert.True(Actor.IgnoresComponent.IsIgnored(2));
                AssertStatus(Assert.Single(Sent), IgnoreStatus.Added);
                Achievements++;
                return true;
            });
            Service = new(clients, Store, achievements, new AccountSessionGate());
        }

        private GameClient? Lookup(string name, GameClient target)
        {
            Lookups++;
            return name == "target" ? target : null;
        }
    }

    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, RecordingProxy>();
        ((RecordingProxy)(object)proxy).Call = invoke;
        return proxy;
    }
    public class RecordingProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
    private sealed class RecordingStore : IPlayerIgnoreStore
    {
        public Func<int, int, bool, Task> BeforeWrite = (_, _, _) => Task.CompletedTask;
        public List<bool> Writes = [];
        public bool Refused;
        public async Task<bool> SetIgnored(int userId, int targetId, bool ignored)
        {
            Writes.Add(ignored);
            await BeforeWrite(userId, targetId, ignored);
            return !Refused;
        }
    }
    private sealed class RecordingService : IPlayerIgnoreService
    {
        public List<string> Calls = [];
        public Task Ignore(GameClient session, string username) { Calls.Add("ignore " + username); return Task.CompletedTask; }
        public Task Unignore(GameClient session, string username) { Calls.Add("unignore " + username); return Task.CompletedTask; }
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
