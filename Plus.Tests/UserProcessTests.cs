using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Process;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

public class UserProcessTests
{
    [Fact]
    public void TickCapturesOneLocalCalendarDayAndPersistsBeforeDailyStateAndPackets()
    {
        var clock = new ManualClock();
        var (habbo, sent) = Player(clock);
        var writes = 0;
        var store = new Store((id, respects, petRespects, day) =>
        {
            Assert.Equal((7, 10, 10, "01/02"), (id, respects, petRespects, day));
            Assert.Equal(("old", 0, 0), (habbo.HabboStats.RespectsTimestamp,
                habbo.HabboStats.DailyRespectPoints, habbo.HabboStats.DailyPetRespectPoints));
            Assert.Empty(sent);
            writes++;
        });
        using var process = Process(clock, store);
        Assert.True(process.Init(habbo));
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Period);
        clock.Fire();
        Assert.Equal(1, clock.Reads);
        Assert.Equal(1, writes);
        Assert.Equal(("01/02", 10, 10), (habbo.HabboStats.RespectsTimestamp,
            habbo.HabboStats.DailyRespectPoints, habbo.HabboStats.DailyPetRespectPoints));
        Assert.Single(sent);
        clock.Fire();
        Assert.Equal(1, writes);
        Assert.Equal(2, clock.Reads);
    }

    [Fact]
    public void FailedDailyPersistenceDoesNotPublishAndTheNextTickCanRecover()
    {
        var clock = new ManualClock();
        var (habbo, sent) = Player(clock);
        var fail = true;
        var logger = new Logger<ProcessComponent>();
        using var process = Process(clock, new Store((_, _, _, _) =>
        { if (fail) throw new InvalidOperationException("forced"); }), logger);
        process.Init(habbo);
        clock.Fire();
        Assert.Equal("old", habbo.HabboStats.RespectsTimestamp);
        Assert.Equal(0, habbo.HabboStats.DailyRespectPoints);
        Assert.Empty(sent);
        Assert.Equal(1, logger.Errors);
        fail = false;
        clock.Fire();
        Assert.Equal("01/02", habbo.HabboStats.RespectsTimestamp);
        Assert.Single(sent);
    }

    [Fact]
    public async Task TimerSuppressesOverlapAndRejectsCallbacksAfterDispose()
    {
        var clock = new ManualClock();
        var (habbo, _) = Player(clock);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writes = 0;
        using var process = Process(clock, new Store((_, _, _, _) =>
        {
            Interlocked.Increment(ref writes);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        }));
        Assert.True(process.Init(habbo));
        Assert.False(process.Init(habbo));
        var first = Task.Run(clock.Fire);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            clock.Fire();
            Assert.Equal(1, Volatile.Read(ref writes));
        }
        finally
        {
            release.Set();
            await first;
        }
        process.Dispose();
        process.Dispose();
        clock.Fire(); // Simulate an already-queued timer callback even after timer disposal.
        Assert.True(clock.TimerDisposed);
        Assert.Equal(1, writes);
        Assert.False(process.Init(habbo));
    }

    [Fact]
    public async Task StatisticsLoginUsesTheSameLocalDayAndPublishesOnlyAfterPersistence()
    {
        var clock = new ManualClock();
        var (habbo, _) = Player(clock);
        var stats = habbo.HabboStats;
        habbo.HabboStats = null!;
        var writes = 0;
        var statistics = Proxy<IHabboStatsService>((method, args) =>
        {
            if (method == "LoadHabboStats") return Task.FromResult(stats);
            Assert.Equal("UpdateDailyRespectsAndTimestamp", method);
            Assert.Equal(new object[] { 7, 10, "01/02" }, args);
            Assert.Null(habbo.HabboStats);
            writes++;
            return Task.CompletedTask;
        });
        await new LoadStatisticsLoginTask(statistics, Proxy<IGroupManager>((_, _) => false),
            clock, TestLogging.For<LoadStatisticsLoginTask>()).Load(habbo);
        Assert.Same(stats, habbo.HabboStats);
        Assert.Equal("01/02", stats.RespectsTimestamp);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(1, writes);
    }

    [RoomComponentDatabaseFact]
    public void DailyRespectStoreWritesDistinctCountsAndCapturedCalendarDay()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        var schema = "task_user_process_" + Guid.NewGuid().ToString("N")[..12];
        options.Database = "information_schema";
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        admin.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            options.Database = schema;
            options.AllowZeroDateTime = true;
            options.ConvertZeroDateTime = true;
            var database = new HabbiconDatabaseTests.TestDatabase(options.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("CREATE TABLE user_statistics (id INT PRIMARY KEY, DailyRespectPoints INT, DailyPetRespectPoints INT, respectsTimestamp VARCHAR(5)); INSERT INTO user_statistics VALUES (7,0,0,'old')");
            new UserProcessStore(database).ResetDailyRespects(7, 12, 8, "01/02");
            Assert.Equal((12, 8, "01/02"), connection.QuerySingle<(int, int, string)>(
                "SELECT DailyRespectPoints,DailyPetRespectPoints,respectsTimestamp FROM user_statistics WHERE id=7"));
        }
        finally { admin.Execute($"DROP DATABASE `{schema}`"); }
    }

    private static (Habbo Habbo, List<(uint Header, byte[] Payload)> Sent) Player(TimeProvider clock)
    {
        var habbo = new Habbo { Id = 7, Username = "user", Gender = "M", Look = "look", Motto = "", Access = UserAccess.Empty,
            HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "old", 0),
            Effects = new EffectsComponent(clock), CreditsUpdateTick = 100 };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        return (habbo, sent);
    }

    private static ProcessComponent Process(TimeProvider clock, IUserProcessStore store, ILogger<ProcessComponent>? logger = null) =>
        new(logger ?? TestLogging.For<ProcessComponent>(), clock, store,
            Proxy<IAchievementManager>((_, _) => null), Proxy<ISettingsManager>((_, _) => "0"));

    private static T Proxy<T>(Func<string, object?[]?, object?> invoke) where T : class => CatalogSnapshotTestSupport.Proxy<T>(invoke);
    private sealed class Store(Action<int, int, int, string> save) : IUserProcessStore
    { public void ResetDailyRespects(int userId, int respects, int petRespects, string day) => save(userId, respects, petRespects, day); }

    private sealed class ManualClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public int Reads { get; private set; }
        public TimeSpan Period { get; private set; }
        public bool TimerDisposed { get; private set; }
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("plus-nine", TimeSpan.FromHours(9), "test", "test");
        public override DateTimeOffset GetUtcNow() { Reads++; return new(2040, 1, 1, 23, 30, 0, TimeSpan.Zero); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; Period = period; Assert.Equal(dueTime, period); return new Timer(this); }
        public void Fire() => _callback!(_state);
        private sealed class Timer(ManualClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => !owner.TimerDisposed;
            public void Dispose() => owner.TimerDisposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Logger<T> : ILogger<T>
    {
        public int Errors { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        { if (level == LogLevel.Error) Errors++; }
    }
}
