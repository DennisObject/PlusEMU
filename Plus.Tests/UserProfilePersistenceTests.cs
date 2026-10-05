using System.Data;
using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.FigureData;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class UserProfilePersistenceTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void MottoThrottleUsesExactTicksAndOneNonUtcClockSample(int ticksAfterBoundary, bool admitted)
    {
        var now = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new CountingClock(now);
        var previous = now.AddSeconds(-2).AddTicks(-ticksAfterBoundary).ToOffset(TimeSpan.FromHours(11));
        var habbo = new Habbo { Id = 7, Motto = "old", LastMottoUpdatedAt = previous };
        var (session, _) = HabbiconTestSupport.Client(habbo);
        var service = Service(new FailingDatabase(), clock);

        if (admitted)
            Assert.Throws<InvalidOperationException>(() => service.ChangeMotto(session, "new"));
        else
            service.ChangeMotto(session, "new");

        Assert.Equal(1, clock.Reads);
        Assert.Equal("old", habbo.Motto);
        Assert.Equal(previous, habbo.LastMottoUpdatedAt);
        Assert.Equal(admitted ? 0 : 1, habbo.MottoUpdateWarnings);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void FigureThrottleUsesExactTicksAndOneNonUtcClockSample(int ticksAfterBoundary, bool admitted)
    {
        var now = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new CountingClock(now);
        var previous = now.AddSeconds(-2).AddTicks(-ticksAfterBoundary).ToOffset(TimeSpan.FromHours(-9));
        var habbo = new Habbo
        {
            Id = 7, Look = "old", Gender = "m", Clothing = new(), Access = UserAccess.Empty,
            LastClothingUpdatedAt = previous
        };
        var (session, _) = HabbiconTestSupport.Client(habbo);
        var service = Service(new FailingDatabase(), clock);

        if (admitted)
            Assert.Throws<InvalidOperationException>(() => service.UpdateFigure(session, new("m", "new")));
        else
            service.UpdateFigure(session, new("m", "new"));

        Assert.Equal(1, clock.Reads);
        Assert.Equal("old", habbo.Look);
        Assert.Equal("m", habbo.Gender);
        Assert.Equal(previous, habbo.LastClothingUpdatedAt);
        Assert.Equal(admitted ? 0 : 1, habbo.ClothingUpdateWarnings);
    }

    [Fact]
    public void InvalidGenderPreservesDenialTimestampAndPublishesOnlyTheAlert()
    {
        var now = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new CountingClock(now);
        var habbo = new Habbo { Id = 7, Look = "old", Gender = "m", Clothing = new(), Access = UserAccess.Empty };
        var (session, sent) = HabbiconTestSupport.Client(habbo);
        var service = Service(new FailingDatabase(), clock);

        service.UpdateFigure(session, new("x", "requested"));

        Assert.Equal(now, habbo.LastClothingUpdatedAt);
        Assert.Equal("old", habbo.Look);
        Assert.Equal("m", habbo.Gender);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(ServerPacketHeader.BroadcastMessageAlertComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public async Task RealAccountGateSerializesSameAccountProfileWrites()
    {
        var gate = new AccountSessionGate();
        var held = gate.Enter(7);
        var habbo = new Habbo { Id = 7, Motto = "old" };
        var (session, _) = HabbiconTestSupport.Client(habbo);
        var service = Service(new FailingDatabase(), new CountingClock(DateTimeOffset.UtcNow), gate);
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var write = Task.Run(() =>
        {
            attempted.SetResult();
            return Record.Exception(() => service.ChangeMotto(session, "new"));
        });
        Exception? outcome = null;
        try
        {
            await attempted.Task;
            await Task.Delay(50);
            Assert.False(write.IsCompleted);
        }
        finally
        {
            held.Dispose();
            outcome = await write;
        }

        Assert.IsType<InvalidOperationException>(outcome);
        Assert.Equal("old", habbo.Motto);
    }

    [RoomComponentDatabaseFact]
    public void FigureAndMottoPersistBeforePublishingAndRefuseMissingRows()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_user_profile_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("CREATE TABLE users(id INT PRIMARY KEY, look VARCHAR(255) NOT NULL, gender VARCHAR(1) NOT NULL, motto VARCHAR(255) NOT NULL); INSERT INTO users VALUES(7,'old','m','old motto')");
            var calls = new List<string>();
            var clock = new CountingClock(new(2042, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var habbo = new Habbo { Id = 7, Look = "old", Gender = "m", Motto = "old motto", Clothing = new(), Access = UserAccess.Empty };
            var (session, sent) = HabbiconTestSupport.Client(habbo);
            var service = Service(database, clock, calls: calls, callback: name =>
            {
                using var verify = database.Connection();
                if (name == "look quest") Assert.Equal(("processed", "M"), verify.QuerySingle<(string, string)>("SELECT look,gender FROM users WHERE id=7"));
                if (name == "motto reward") Assert.Equal("new motto", verify.QuerySingle<string>("SELECT motto FROM users WHERE id=7"));
            });

            service.UpdateFigure(session, new("m", "requested"));
            service.ChangeMotto(session, "new motto");

            Assert.Equal("filtered", habbo.Look);
            Assert.Equal("m", habbo.Gender);
            Assert.Equal("new motto", habbo.Motto);
            Assert.Equal(clock.Now, habbo.LastClothingUpdatedAt);
            Assert.Equal(clock.Now, habbo.LastMottoUpdatedAt);
            Assert.Equal(2, clock.Reads);
            Assert.Equal(["look quest", "look reward", "look achievement", "motto reward", "motto quest", "motto achievement"], calls);
            var aspect = Assert.Single(sent, packet => packet.Header == ServerPacketHeader.AvatarAspectUpdateComposer);
            var body = new FlashIncomingPacket { Buffer = aspect.Payload };
            Assert.Equal("processed", body.ReadString());
            Assert.Equal("M", body.ReadString());
            Assert.False(body.HasDataRemaining());

            connection.Execute("DELETE FROM users WHERE id=7");
            sent.Clear();
            clock.Advance(TimeSpan.FromSeconds(3));
            var beforeCalls = calls.Count;
            var previousFigureAt = habbo.LastClothingUpdatedAt;
            var previousMottoAt = habbo.LastMottoUpdatedAt;
            Assert.Throws<DBConcurrencyException>(() => service.UpdateFigure(session, new("f", "second")));
            Assert.Throws<DBConcurrencyException>(() => service.ChangeMotto(session, "missing"));
            Assert.Equal("filtered", habbo.Look);
            Assert.Equal("new motto", habbo.Motto);
            Assert.Equal(previousFigureAt, habbo.LastClothingUpdatedAt);
            Assert.Equal(previousMottoAt, habbo.LastMottoUpdatedAt);
            Assert.Equal(beforeCalls, calls.Count);
            Assert.Empty(sent);
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static UserProfileService Service(IDatabase database, TimeProvider clock,
        IAccountSessionGate? gate = null, List<string>? calls = null, Action<string>? callback = null)
    {
        calls ??= [];
        callback ??= _ => { };
        return new(
            Proxy<IFigureDataManager>((method, _) => method switch
            {
                nameof(IFigureDataManager.ProcessFigure) => "processed",
                nameof(IFigureDataManager.FilterFigure) => "filtered",
                _ => null
            }),
            Proxy<IAchievementManager>((method, _) =>
            {
                if (method == nameof(IAchievementManager.ProgressAchievement))
                {
                    var name = calls.Count < 3 ? "look achievement" : "motto achievement";
                    callback(name); calls.Add(name); return true;
                }
                return null;
            }),
            Proxy<IQuestManager>((method, args) =>
            {
                if (method == nameof(IQuestManager.ProgressUserQuest))
                {
                    var name = (QuestType)args[1] == QuestType.ProfileChangeLook ? "look quest" : "motto quest";
                    callback(name); calls.Add(name);
                }
                return null;
            }),
            Proxy<IWordFilterManager>((method, args) => method == nameof(IWordFilterManager.CheckMessage) ? args[0] : null),
            database, clock, null!,
            Proxy<IRewardTrackManager>((method, args) =>
            {
                if (method == nameof(IRewardTrackManager.Progress))
                {
                    var name = (string)args[1] == RewardTrackActions.ChangeFigure ? "look reward" : "motto reward";
                    callback(name); calls.Add(name);
                }
                return null;
            }), gate ?? new AccountSessionGate());
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Call(targetMethod!.Name, args!) ??
            (targetMethod.ReturnType.IsValueType && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null);
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now => now;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private sealed class FailingDatabase : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => throw new InvalidOperationException("Persistence failed");
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
