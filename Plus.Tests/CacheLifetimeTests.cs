using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Process;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
public sealed class CacheLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2040, 4, 5, 6, 7, 8, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void CachedUserExpiresAtExactThirtyMinuteUtcBoundary(int deltaMilliseconds, bool expected)
    {
        var user = new CachedUser { Id = 1, RefreshedAt = Now };
        var observed = Now.AddMinutes(30).AddMilliseconds(deltaMilliseconds).ToOffset(TimeSpan.FromHours(9));

        Assert.Equal(expected, user.IsExpiredAt(observed));
    }

    [Fact]
    public void GenerateUserCapturesOneInstantAndAtomicallyReplacesTheCachedValue()
    {
        var clock = new CountingClock(Now);
        var habbo = new Habbo { Id = 7, Username = "online", Motto = "motto", Look = "look" };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        var manager = Manager(clock, Clients((7, client)));

        var initial = Assert.IsType<CachedUser>(manager.GenerateUser(7));
        Assert.Equal(Now, initial.RefreshedAt);
        Assert.Equal(1, clock.Reads);

        clock.UtcNow = Now.AddMinutes(10);
        clock.ResetReads();
        var refreshed = Assert.IsType<CachedUser>(manager.GenerateUser(7));

        Assert.NotSame(initial, refreshed);
        Assert.Equal(clock.UtcNow, refreshed.RefreshedAt);
        Assert.Equal(1, clock.Reads);
        Assert.Same(refreshed, Assert.IsType<CachedUser>(Get(manager, 7)));
    }

    [Fact]
    public void CapturedSweepEntryCannotRemoveARefreshedOrReplacementValue()
    {
        var clock = new CountingClock(Now);
        var habbo = new Habbo { Id = 8, Username = "first" };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        var manager = Manager(clock, Clients((8, client)));
        var captured = Assert.IsType<CachedUser>(manager.GenerateUser(8));
        var sweepAt = Now.AddMinutes(30);

        clock.UtcNow = sweepAt;
        var refreshed = Assert.IsType<CachedUser>(manager.GenerateUser(8));
        Assert.False(manager.RemoveIfExpired(new(8, captured), sweepAt));
        Assert.Same(refreshed, Get(manager, 8));

        Assert.True(manager.TryRemoveUser(8, out _));
        var replacement = Assert.IsType<CachedUser>(manager.GenerateUser(8));
        Assert.NotSame(refreshed, replacement);
        Assert.False(manager.RemoveIfExpired(new(8, refreshed), sweepAt));
        Assert.Same(replacement, Get(manager, 8));
    }

    [Fact]
    public void OwnerSweepCapturesClockOnceAndRemovesOnlyExpiredCachedUsers()
    {
        var clock = new CountingClock(Now);
        var process = new RecordingProcess();
        var firstClient = HabbiconTestSupport.Client(new Habbo { Id = 1 }).Client;
        var secondClient = HabbiconTestSupport.Client(new Habbo { Id = 2 }).Client;
        var manager = Manager(clock, Clients((1, firstClient), (2, secondClient)), process);
        clock.UtcNow = Now.AddMinutes(-30);
        manager.GenerateUser(1);
        clock.UtcNow = Now.AddMinutes(-30).AddTicks(1);
        manager.GenerateUser(2);
        clock.UtcNow = Now;
        manager.Init();
        clock.ResetReads();

        process.Sweep!();

        Assert.Equal(1, clock.Reads);
        Assert.False(manager.ContainsUser(1));
        Assert.True(manager.ContainsUser(2));
    }

    [Fact]
    public void LegacyCacheUsesExplicitUtcInstantAndUnknownRemainsExpired()
    {
        var unknown = new Habbo();
        Assert.True(unknown.CacheExpiredAt(Now.ToOffset(TimeSpan.FromHours(-7))));

        var cached = new Habbo();
        typeof(Habbo).GetField("_cachedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cached, Now);
        Assert.False(cached.CacheExpiredAt(Now.AddMinutes(30).AddTicks(-1).ToOffset(TimeSpan.FromHours(9))));
        Assert.True(cached.CacheExpiredAt(Now.AddMinutes(30).ToOffset(TimeSpan.FromHours(-7))));
    }

    [Fact]
    public async Task ProcessTimerPreventsOverlapRecoversFromErrorsAndStopsAfterDispose()
    {
        var clock = new ManualTimerClock(Now);
        var logger = new RecordingLogger<ProcessComponent>();
        var process = new ProcessComponent(logger, clock);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        process.Init(() =>
        {
            var call = Interlocked.Increment(ref calls);

            if (call == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
            else if (call == 2)
            {
                throw new InvalidOperationException("probe");
            }
        });
        Assert.Equal(TimeSpan.FromMinutes(20), clock.DueTime);
        Assert.Equal(TimeSpan.FromMinutes(20), clock.Period);

        var first = Task.Run(clock.Fire);

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            clock.Fire();
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
            await first;
        }

        clock.Fire();
        clock.Fire();
        Assert.Equal(3, calls);
        Assert.Equal(1, logger.Errors);

        process.Dispose();
        clock.FireEvenIfDisposed();
        Assert.Equal(3, calls);
        Assert.True(clock.TimerDisposed);
    }

    private static CacheManager Manager(CountingClock clock, IGameClientManager clients, RecordingProcess? process = null) =>
        new(process ?? new(), new ThrowingDatabase(), clients, TestLogging.For<CacheManager>(), clock);

    private static CachedUser? Get(CacheManager manager, int id) => manager.TryGetUser(id, out var user) ? user : null;

    private static IGameClientManager Clients(params (int Id, GameClient Client)[] clients)
    {
        var proxy = DispatchProxy.Create<IGameClientManager, ClientLookup>();
        ((ClientLookup)(object)proxy).Clients = clients.ToDictionary(x => x.Id, x => x.Client);

        return proxy;
    }

    private class ClientLookup : DispatchProxy
    {
        public Dictionary<int, GameClient> Clients { get; set; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(IGameClientManager.GetClientByUserId) => Clients.GetValueOrDefault((int)args![0]!),
            _ => throw new NotSupportedException(method.Name)
        };
    }

    private sealed class ThrowingDatabase : IDatabase
    {
        public bool IsConnected() => throw new NotSupportedException();
        public IDbConnection Connection() => throw new InvalidOperationException("Database lookup was not expected.");
    }

    private sealed class RecordingProcess : IProcessComponent
    {
        public Action? Sweep
        {
            get; private set;
        }
        public void Init(Action sweep) => Sweep = sweep;
        public void Dispose()
        {
        }
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public int Reads
        {
            get; private set;
        }
        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("cache-plus-nine", TimeSpan.FromHours(9), "test", "test");
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return UtcNow;
        }
        public void ResetReads() => Reads = 0;
    }

    private sealed class ManualTimerClock(DateTimeOffset now) : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        private ManualTimer? _timer;
        public TimeSpan DueTime
        {
            get; private set;
        }
        public TimeSpan Period
        {
            get; private set;
        }
        public bool TimerDisposed => _timer?.Disposed == true;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            DueTime = dueTime;
            Period = period;

            return _timer = new();
        }
        public void Fire()
        {
            if (_timer?.Disposed != true)
            {
                _callback!(_state);
            }
        }
        public void FireEvenIfDisposed() => _callback!(_state);

        private sealed class ManualTimer : ITimer
        {
            public bool Disposed
            {
                get; private set;
            }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public int Errors
        {
            get; private set;
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors++;
            }
        }
    }
}
