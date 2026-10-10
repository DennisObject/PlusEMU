using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.UserData;
using Plus.HabboHotel.Moderation;
using Xunit;

namespace Plus.Tests;

public sealed class TradingLockDatabaseFactAttribute : FactAttribute
{
    public TradingLockDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_REFACTOR_TEST_CONNECTION_STRING"))) {
            Skip = "Set PLUS_REFACTOR_TEST_CONNECTION_STRING to a disposable task_refactor_tests_ database with the Plus user schema.";
        }
    }
}

[Collection("SharedDatabase")]
public sealed class TradingLockDatabaseTests : IDisposable
{
    private const int UserId = 935001;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly SharedTestClients _clients = new();
    private readonly Clock _clock = new();

    public TradingLockDatabaseTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("PLUS_REFACTOR_TEST_CONNECTION_STRING")!;

        if (!new MySqlConnectionStringBuilder(connectionString).Database.StartsWith("task_refactor_tests_", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Trading lock tests require a disposable task_refactor_tests_ schema.");
        }

        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        _database = new(connectionString);
        Dispose();
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO users (id,username,auth_ticket,last_online,account_created) VALUES (@UserId,'trade_probe','',@now,@now); " +
            "INSERT INTO users_settings(user_id) VALUES (@UserId)", new { UserId, now = _clock.Now.UtcDateTime });
    }

    public void Dispose()
    {
        using var connection = _database.Connection();
        connection.Execute("DELETE FROM user_info WHERE user_id=@UserId; DELETE FROM users_settings WHERE user_id=@UserId; DELETE FROM users WHERE id=@UserId", new { UserId });
    }

    [TradingLockDatabaseFact]
    public async Task FutureUtcLockLoadsForLoginAndAdministration()
    {
        var habbo = Online();
        var locks = Locks();
        var expiry = locks.Set(UserId, TimeSpan.FromDays(2));
        var factory = new UserDataFactory(null!, _database, [], null!, null!, null!, new Plus.HabboHotel.Rooms.RoomVisitRecorder(_database, TimeProvider.System), new FixedTimeProvider(FixedTimeProvider.Epoch), TestRoomAchievements.Unused, TestGameClientManager.Empty, TestRoomManager.Unused);

        Assert.Equal(TimeSpan.Zero, expiry.Offset);
        Assert.Equal(expiry, habbo.TradingLockExpiresAt);
        Assert.Equal(expiry, (await factory.GetUserDataByIdAsync(UserId))!.TradingLockExpiresAt);
        Assert.Equal(expiry, new ModerationUserStore(_database).Find(UserId)!.TradingLockExpiresAt);
        Assert.True(locks.IsLocked(habbo));
        locks.Clear(UserId);
        Assert.Null(habbo.TradingLockExpiresAt);
        Assert.Null(new ModerationUserStore(_database).Find(UserId)!.TradingLockExpiresAt);
    }

    [TradingLockDatabaseFact]
    public void LockExpiryFloorsToNativeUtcSecondsAndReadsTheClockOnce()
    {
        var habbo = Online();
        _clock.Now = _clock.Now.AddTicks(1_234_567);
        int before = _clock.Reads;
        var expiry = Locks().Set(UserId, TimeSpan.FromSeconds(2.5));
        Assert.Equal(before + 1, _clock.Reads);
        Assert.Equal(new DateTimeOffset(2042, 1, 1, 10, 0, 2, TimeSpan.Zero), expiry);
        Assert.Equal(expiry, habbo.TradingLockExpiresAt);
        using var connection = _database.Connection();
        Assert.Equal(expiry, connection.QuerySingle<DateTimeOffset>(
            "SELECT trading_locked FROM user_info WHERE user_id=@UserId", new { UserId }));
    }

    [TradingLockDatabaseFact]
    public void OldExpiredSessionCannotClearNewerSanctionAndExpiryUsesNull()
    {
        var habbo = Online();
        var locks = Locks();
        var expiry = locks.Set(UserId, TimeSpan.FromDays(2));
        habbo.TradingLockExpiresAt = _clock.Now.AddSeconds(-1);

        Assert.True(locks.IsLocked(habbo));
        Assert.Equal(expiry, habbo.TradingLockExpiresAt);
        _clock.Now = expiry;
        Assert.False(locks.IsLocked(habbo));
        using var connection = _database.Connection();
        Assert.Null(connection.QuerySingle<DateTime?>("SELECT trading_locked FROM user_info WHERE user_id=@UserId", new { UserId }));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT trading_locks_count FROM user_info WHERE user_id=@UserId", new { UserId }));
    }

    private TradingLockService Locks() => new(_database, _clients, new AccountSessionGate(), _clock);
    private Habbo Online()
    {
        var habbo = new Habbo { Id = UserId };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        _clients.Online[UserId] = client;

        return habbo;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2042, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }
}
