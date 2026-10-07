using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Process;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
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
        {
            if (fail) {
                throw new InvalidOperationException("forced");
            }
        }), logger);
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

        try {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            clock.Fire();
            Assert.Equal(1, Volatile.Read(ref writes));
        }
        finally {
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
    public void SavedWalletRejectsDailyResetAndItsPublication()
    {
        var clock = new ManualClock();
        var (habbo, sent) = Player(clock);
        var writes = 0;
        habbo.Persistence = Proxy<IUserPersistenceService>((_, _) => null);
        habbo.Save();
        using var process = Process(clock, new Store((_, _, _, _) => writes++));
        Assert.True(process.Init(habbo));

        clock.Fire();

        Assert.Equal(0, writes);
        Assert.Equal(("old", 0, 0), (habbo.HabboStats.RespectsTimestamp,
            habbo.HabboStats.DailyRespectPoints, habbo.HabboStats.DailyPetRespectPoints));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task AdmittedTickWaitingForWalletCannotResetAfterDisconnect()
    {
        var clock = new ManualClock();
        var (habbo, sent) = Player(clock);
        using var admitted = new ManualResetEventSlim();
        clock.OnRead = admitted.Set;
        var writes = 0;
        using var process = Process(clock, new Store((_, _, _, _) => Interlocked.Increment(ref writes)));
        using var disconnect = new DisconnectContext(habbo, process, Proxy<IUserPersistenceService>((_, _) => null));
        Task tick;

        lock (habbo.WalletSync) {
            tick = Task.Run(clock.Fire);
            Assert.True(admitted.Wait(TimeSpan.FromSeconds(5)));
            habbo.OnDisconnect();
        }

        await tick.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(clock.TimerDisposed);
        Assert.Equal(0, writes);
        Assert.Equal("old", habbo.HabboStats.RespectsTimestamp);
        Assert.Empty(sent);
        Assert.Equal(1, disconnect.Unregisters);
    }

    [Fact]
    public async Task ResetPacketCanDisconnectAndDisposeItsOwnProcess()
    {
        var clock = new ManualClock();
        var (habbo, sent) = Player(clock);
        (string Day, int Respects, int PetRespects)? saved = null;
        var saves = 0;
        using var process = Process(clock, new Store((_, _, _, _) => { }));
        using var disconnect = new DisconnectContext(habbo, process, Proxy<IUserPersistenceService>((method, _) =>
        {
            Assert.Equal("Save", method);
            saved = (habbo.HabboStats.RespectsTimestamp, habbo.HabboStats.DailyRespectPoints,
                habbo.HabboStats.DailyPetRespectPoints);
            saves++;

            return null;
        }));
        var send = habbo.Client.SendCallback;
        habbo.Client.SendCallback = args =>
        {
            var result = send(args);
            habbo.OnDisconnect();

            return result;
        };

        await Task.Run(clock.Fire).WaitAsync(TimeSpan.FromSeconds(5));
        habbo.OnDisconnect();
        clock.Fire();

        Assert.Equal(("01/02", 10, 10), saved);
        Assert.Equal(1, saves);
        Assert.Equal(1, disconnect.Unregisters);
        Assert.True(clock.TimerDisposed);
        Assert.Null(habbo.Client);
        Assert.Single(sent);
    }

    [Fact]
    public void FailedDisconnectSaveStillReleasesTheUserAndReportsTheFailure()
    {
        var clock = new ManualClock();
        var (habbo, _) = Player(clock);
        using var process = Process(clock, new Store((_, _, _, _) => { }));
        var saves = 0;
        using var disconnect = new DisconnectContext(habbo, process, Proxy<IUserPersistenceService>((_, _) =>
        {
            saves++;
            throw new InvalidOperationException("Save failed");
        }));
        var disposed = 0;
        habbo.Disposed += (_, _) => disposed++;

        var exception = Assert.Throws<InvalidOperationException>(habbo.OnDisconnect);
        habbo.OnDisconnect();

        Assert.Equal("Save failed", exception.Message);
        Assert.Equal(1, saves);
        Assert.Equal(1, disconnect.Unregisters);
        Assert.Equal(1, disposed);
        Assert.True(clock.TimerDisposed);
        Assert.Null(habbo.Client);
    }

    [Fact]
    public void TransportDisconnectContainsAndLogsAFailedUserSave()
    {
        var clock = new ManualClock();
        var (habbo, _) = Player(clock);
        var logger = new Logger<GameClient>();
        var client = new Plus.Communication.Flash.FlashGameClient(TestGameServer.Instance,
            new Plus.Communication.Flash.FlashPacketFactory(), logger);
        client.SetHabbo(habbo);
        habbo.Client = client;
        using var process = Process(clock, new Store((_, _, _, _) => { }));
        using var disconnect = new DisconnectContext(habbo, process, Proxy<IUserPersistenceService>((_, _) =>
            throw new InvalidOperationException("Save failed")));

        client.OnDisconnected();
        client.OnDisconnected();

        Assert.Equal(1, logger.Errors);
        Assert.Equal(1, disconnect.Unregisters);
        Assert.True(clock.TimerDisposed);
        Assert.Null(habbo.Client);
        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public void TransportDisconnectCleansUpOffTheTransportThread()
    {
        var clock = new ManualClock();
        var (habbo, _) = Player(clock);
        var client = new Plus.Communication.Flash.FlashGameClient(TestGameServer.Instance,
            new Plus.Communication.Flash.FlashPacketFactory(), new Logger<GameClient>());
        client.SetHabbo(habbo);
        habbo.Client = client;
        // Stands in for NetCoreServer's send lock, which a Wired cycle sending to this session also waits for.
        var sendLock = new object();
        using var saved = new ManualResetEventSlim();
        using var process = Process(clock, new Store((_, _, _, _) => { }));
        using var disconnect = new DisconnectContext(habbo, process, Proxy<IUserPersistenceService>((_, _) =>
        {
            lock (sendLock) {
                saved.Set();
            }

            return null;
        }));

        lock (sendLock) {
            client.OnTransportDisconnected();

            Assert.True(client.Closed.IsCancellationRequested);
            Assert.False(saved.IsSet);
        }

        Assert.True(saved.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(SpinWait.SpinUntil(() => habbo.Client == null, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, disconnect.Unregisters);
    }

    [RoomComponentDatabaseFact]
    public async Task DisconnectSavesTheCommittedDailyResetBeforeUnregistering()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_user_process_shutdown_" + Guid.NewGuid().ToString("N");
        using var admin = new MySqlConnection(root);
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new HabbiconDatabaseTests.TestDatabase(new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString);
            using var connection = database.Connection();
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

            foreach (var table in new[] { "users", "users_settings", "user_stats" }) {
                var definition = System.Text.RegularExpressions.Regex.Match(pristine,
                    $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
                Assert.NotEmpty(definition);
                connection.Execute(definition);
            }

            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/5_RenameUserStatsTable.sql")));
            connection.Execute("ALTER TABLE users ADD bubble_id TINYINT NOT NULL DEFAULT 0");
            connection.Execute("INSERT INTO users(id,username,auth_ticket) VALUES(7,'user','ticket'); " +
                "INSERT INTO users_settings(user_id) VALUES(7); " +
                "INSERT INTO user_statistics(id,DailyRespectPoints,DailyPetRespectPoints,respectsTimestamp) VALUES(7,0,0,'old')");

            var clock = new ManualClock();
            var (habbo, sent) = Player(clock);
            habbo.SessionStartedAt = clock.GetUtcNow();
            using var committed = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var disconnectStarted = new CountdownEvent(2);
            var realStore = new UserProcessStore(database);
            using var process = Process(clock, new Store((id, respects, petRespects, day) =>
            {
                realStore.ResetDailyRespects(id, respects, petRespects, day);
                committed.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }));
            using var disconnect = new DisconnectContext(habbo, process, new UserPersistenceService(database, clock));
            var tick = Task.Run(clock.Fire);
            Task? logout = null;
            bool savedBeforeResetPublished;

            try {
                Assert.True(committed.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal((10, 10, "01/02"), StoredRespects());
                Assert.Equal(("old", 0, 0), (habbo.HabboStats.RespectsTimestamp,
                    habbo.HabboStats.DailyRespectPoints, habbo.HabboStats.DailyPetRespectPoints));
                logout = Task.WhenAll(Task.Run(Disconnect), Task.Run(Disconnect));
                Assert.True(disconnectStarted.Wait(TimeSpan.FromSeconds(5)));
                savedBeforeResetPublished = await Task.WhenAny(logout, Task.Delay(100)) == logout;
                Assert.Empty(sent);
            }
            finally {
                release.Set();
                await tick.WaitAsync(TimeSpan.FromSeconds(5));

                if (logout != null) {
                    await logout.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }

            Assert.Equal((10, 10, "01/02"), StoredRespects());
            Assert.False(savedBeforeResetPublished);
            Assert.Equal(1, disconnect.Unregisters);
            Assert.Null(habbo.Client);
            Assert.Single(sent);
            habbo.OnDisconnect();
            clock.Fire();
            Assert.Equal(1, disconnect.Unregisters);
            Assert.Single(sent);

            (int, int, string) StoredRespects() => connection.QuerySingle<(int, int, string)>(
                "SELECT DailyRespectPoints,DailyPetRespectPoints,respectsTimestamp FROM user_statistics WHERE id=7");
            void Disconnect()
            {
                disconnectStarted.Signal();
                habbo.OnDisconnect();
            }
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
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
            if (method == "LoadHabboStats") {
                return Task.FromResult(stats);
            }

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

        try {
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
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static (Habbo Habbo, List<(uint Header, byte[] Payload)> Sent) Player(TimeProvider clock)
    {
        var habbo = new Habbo
        {
            Id = 7,
            Username = "user",
            Gender = "M",
            Look = "look",
            Motto = "",
            Access = UserAccess.Empty,
            HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "old", 0),
            Effects = new EffectsComponent(clock),
            CreditsUpdateTick = 100
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;

        return (habbo, sent);
    }

    private static ProcessComponent Process(TimeProvider clock, IUserProcessStore store, ILogger<ProcessComponent>? logger = null) =>
        new(logger ?? TestLogging.For<ProcessComponent>(), clock, store,
            Proxy<IAchievementManager>((_, _) => null), Proxy<ISettingsManager>((_, _) => "0"));

    private static T Proxy<T>(Func<string, object?[]?, object?> invoke) where T : class => CatalogSnapshotTestSupport.Proxy<T>(invoke);
    private sealed class Store(Action<int, int, int, string> save) : IUserProcessStore
    {
        public void ResetDailyRespects(int userId, int respects, int petRespects, string day) => save(userId, respects, petRespects, day);
    }

    private sealed class DisconnectContext : IDisposable
    {
        private readonly System.Reflection.FieldInfo _game = typeof(PlusEnvironment).GetField("_game",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        private readonly object? _previous;
        public int Unregisters { get; private set; }

        public DisconnectContext(Habbo habbo, ProcessComponent process, IUserPersistenceService persistence)
        {
            _previous = _game.GetValue(null);
            habbo.Persistence = persistence;
            Assert.True(habbo.InitProcess(Proxy<IUserProcessFactory>((_, _) => process)));
            var clients = Proxy<IGameClientManager>((method, _) =>
            {
                Assert.Equal("UnregisterClient", method);
                Unregisters++;

                return null;
            });
            _game.SetValue(null, Proxy<IGame>((method, _) => method == "get_ClientManager"
                ? clients : throw new InvalidOperationException(method)));
        }

        public void Dispose() => _game.SetValue(null, _previous);
    }

    private sealed class ManualClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public int Reads { get; private set; }
        public TimeSpan Period { get; private set; }
        public bool TimerDisposed { get; private set; }
        public Action? OnRead { get; set; }
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("plus-nine", TimeSpan.FromHours(9), "test", "test");
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            OnRead?.Invoke();

            return new(2040, 1, 1, 23, 30, 0, TimeSpan.Zero);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            Period = period;
            Assert.Equal(dueTime, period);

            return new Timer(this);
        }
        public void Fire() => _callback!(_state);
        private sealed class Timer(ManualClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => !owner.TimerDisposed;
            public void Dispose() => owner.TimerDisposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
    private sealed class Logger<T> : ILogger<T>
    {
        public int Errors { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (level == LogLevel.Error) {
                Errors++;
            }
        }
    }
}
