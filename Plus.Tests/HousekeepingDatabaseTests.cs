using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.UserData;
using Plus.Utilities;
using Xunit;

namespace Plus.Tests;

public sealed class HousekeepingDatabaseFactAttribute : FactAttribute
{
    public HousekeepingDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING")))
            Skip = "Set PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING to a disposable task_housekeeping_tests_ database with the full Plus schema.";
    }
}

// Runs against a disposable schema built from Original Database.sql and every update, including 20_Housekeeping.sql.
[Collection("HousekeepingDatabase")]
public class HousekeepingDatabaseTests
{
    private const int Owner = 920001, Target = 920002, Peer = 920003;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly HousekeepingUserStore _users;
    private readonly HousekeepingActionTests.FakeClients _clients = new();

    public HousekeepingDatabaseTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING")!;
        if (!new MySqlConnectionStringBuilder(connectionString).Database.StartsWith("task_housekeeping_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Housekeeping database tests require a disposable task_housekeeping_tests_ schema.");
        _database = new(connectionString);
        Execute("DELETE FROM users WHERE id BETWEEN 920000 AND 920099; DELETE FROM user_info WHERE user_id BETWEEN 920000 AND 920099; " +
                "DELETE FROM rooms WHERE id BETWEEN 920000 AND 920099; DELETE FROM bans WHERE value LIKE 'hk\\_%'; DELETE FROM housekeeping_log; " +
                "DELETE FROM housekeeping_online_peaks; DELETE FROM user_club_memberships WHERE user_id BETWEEN 920000 AND 920099");
        Execute("INSERT INTO users (id, username, auth_ticket, `rank`, credits, activity_points, vip_points, mail, ip_last, online) VALUES " +
                $"({Owner}, 'hk_owner', '', 9, 0, 0, 0, 'owner@hotel', '10.0.0.1', 0), ({Target}, 'hk_o''brien', 'old-ticket', 1, 100, 50, 5, 'target@hotel', '10.0.0.2', 0), " +
                $"({Peer}, 'hk_peer', '', 9, 0, 0, 0, '', '', 1)");
        Execute($"INSERT INTO user_info (user_id, trading_locked) VALUES ({Target}, 0)");
        _users = new(_database);
    }

    private void Execute(string sql, object? parameters = null)
    {
        using var connection = _database.Connection();
        connection.Execute(sql, parameters);
    }

    private T Scalar<T>(string sql)
    {
        using var connection = _database.Connection();
        return connection.ExecuteScalar<T>(sql)!;
    }

    private static Habbo Staff(int rank = 9) => new() { Id = Owner, Username = "hk_owner", Rank = rank, Permissions = new(new(), new()) };

    [HousekeepingDatabaseFact]
    public void MigrationIsIdempotentAndOnlyGrantsRanksHoldingModBanAny()
    {
        var migration = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/20_Housekeeping.sql"));
        Execute(migration);
        Execute(migration);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM permissions WHERE permission = 'acc_housekeeping'"));
        var groups = Scalar<string>("SELECT GROUP_CONCAT(DISTINCT r.group_id ORDER BY r.group_id) FROM permissions_rights r JOIN permissions p ON p.id = r.permission_id WHERE p.permission = 'housekeeping_economy'");
        var anchors = Scalar<string>("SELECT GROUP_CONCAT(DISTINCT r.group_id ORDER BY r.group_id) FROM permissions_rights r JOIN permissions p ON p.id = r.permission_id WHERE p.permission = 'mod_ban_any'");
        Assert.Equal(anchors, groups);
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM (SELECT group_id, permission_id FROM permissions_rights GROUP BY group_id, permission_id HAVING COUNT(*) > 1) duplicates"));
    }

    [HousekeepingDatabaseFact]
    public void PermissionGroupsExposeTheirRealNameAndBadge()
    {
        var permissions = new PermissionManager(_database, NullLogger<PermissionManager>.Instance);
        permissions.Init();
        Assert.True(permissions.TryGetGroup(9, out var owner));
        Assert.Equal(("Owner", "OWNR"), (owner.Name, owner.Badge));
    }

    [HousekeepingDatabaseFact]
    public void UserLookupsReadAccountRowsByIdAndName()
    {
        var user = _users.Find("hk_o'brien")!;
        Assert.Equal((Target, 1, 100, 50, 5, "target@hotel", "10.0.0.2"), (user.Id, user.Rank, user.Credits, user.Duckets, user.Diamonds, user.Mail, user.IpLast));
        Assert.Equal(9, _users.Find(Owner)!.Rank);
        Assert.Null(_users.Find(920099));
    }

    [HousekeepingDatabaseFact]
    public void AuditRowsRoundTripNewestFirstWithoutLineBreaks()
    {
        var audit = new HousekeepingAuditLog(_database);
        audit.Write(Owner, "hk_owner", "user.ban", HousekeepingOutcome.Success(HousekeepingTarget.User(Target, "hk_o'brien"), "hours=2 reason=a\nb"));
        audit.Write(Owner, "hk_owner", "room.close", HousekeepingOutcome.Fail(HousekeepingErrors.RankTooHigh, HousekeepingTarget.Room(5, "Lobby")));
        var rows = audit.List(10);
        Assert.Equal(new[] { "room.close", "user.ban" }, rows.Select(row => row.Action));
        Assert.Equal(("room", 5, "Lobby", false), (rows[0].TargetType, rows[0].TargetId, rows[0].TargetLabel, rows[0].Success));
        Assert.Equal(("user", "hk_o'brien", "hours=2 reason=a b", true), (rows[1].TargetType, rows[1].TargetLabel, rows[1].Detail, rows[1].Success));
        Assert.True(rows[1].Timestamp > 1_700_000_000);
    }

    [HousekeepingDatabaseFact]
    public void OfflineGrantsUpdateTheRowAndRejectOverflow()
    {
        var economy = new HousekeepingEconomyActions(_users, _clients, null!, null!, null!, _database, new AccountSessionGate());
        Assert.True(economy.Give(Staff(), Target, HousekeepingCurrency.Duckets, 25).Ok);
        Assert.Equal(75, Scalar<int>($"SELECT activity_points FROM users WHERE id = {Target}"));
        Execute($"UPDATE users SET vip_points = {int.MaxValue - 10} WHERE id = {Target}");
        Assert.Equal(HousekeepingErrors.EconomyFailed, economy.Give(Staff(), Target, HousekeepingCurrency.Diamonds, 11).Message);
        Assert.Equal(int.MaxValue - 10, Scalar<int>($"SELECT vip_points FROM users WHERE id = {Target}"));
    }

    [HousekeepingDatabaseFact]
    public void ClubGrantsExtendRunningMembershipsAndZeroEndsThem()
    {
        var clubs = new ClubMembershipService(_database);
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Execute("INSERT INTO user_club_memberships (user_id, expires_at) VALUES (@Target, @expires)", new { Target, expires = now + 86400 });
        Assert.InRange(clubs.Grant(Target, 2), now + 86400 * 3, now + 86400 * 3 + 5);
        Assert.InRange(clubs.Grant(Target, 0), now, now + 5);
        Assert.InRange(clubs.Grant(Owner, 1), now + 86400, now + 86405);
    }

    [HousekeepingDatabaseFact]
    public void BansUseParametersAndUnbanRemovesTheRow()
    {
        var moderation = new ModerationManager(_database, NullLogger<ModerationManager>.Instance);
        moderation.BanUser("hk_owner", ModerationBanType.Username, "hk_o'brien", "it's spam", UnixTimestamp.GetNow() + 3600);
        Assert.True(moderation.IsBanned("hk_o'brien", out _));
        Assert.Equal("it's spam", Scalar<string>("SELECT reason FROM bans WHERE value = 'hk_o''brien'"));
        Assert.True(moderation.UnbanUser("hk_o'brien"));
        Assert.False(moderation.IsBanned("hk_o'brien", out _));
        Assert.False(moderation.UnbanUser("hk_o'brien"));
    }

    [HousekeepingDatabaseFact]
    public void PasswordResetStoresOnlyAHashAndRevokesTheSsoTicket()
    {
        var hasher = new Argon2idPasswordHasher();
        var actions = new HousekeepingUserActions(_users, _clients, null!, null!, null!, hasher, _database, new AccountSessionGate());
        var outcome = actions.ResetPassword(Staff(), Target);
        Assert.True(outcome.Ok);
        var stored = Scalar<string>($"SELECT password FROM users WHERE id = {Target}");
        Assert.DoesNotContain(outcome.Message, stored);
        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(outcome.Message, stored));
        Assert.Equal("", Scalar<string>($"SELECT auth_ticket FROM users WHERE id = {Target}"));
        Assert.DoesNotContain(outcome.Message, outcome.Detail);
        Assert.Equal(HousekeepingErrors.RankTooHigh, actions.ResetPassword(Staff(), Peer).Message);
    }

    [HousekeepingDatabaseFact]
    public void OfflineSanctionsPersistMuteAndTradeLock()
    {
        var actions = new HousekeepingUserActions(_users, _clients, null!, null!, null!, null!, _database, new AccountSessionGate());
        Assert.True(actions.Mute(Staff(), Target, "", 15).Ok);
        Assert.Equal(900, Scalar<double>($"SELECT time_muted FROM users WHERE id = {Target}"));
        Assert.True(actions.TradeLock(Staff(), Target, 2, "").Ok);
        Assert.True(Scalar<double>($"SELECT trading_locked FROM user_info WHERE user_id = {Target}") > UnixTimestamp.GetNow() + 7000);
        Assert.Equal(1, Scalar<int>($"SELECT trading_locks_count FROM user_info WHERE user_id = {Target}"));
        var record = _users.Find(Target)!;
        Assert.True(record.TimeMuted > 0 && record.TradingLocked > 0);
    }

    [HousekeepingDatabaseFact]
    public void RoomSearchesEscapeLikeAndReadOwners()
    {
        Execute("INSERT INTO rooms (id, roomtype, caption, description, owner, state, users_now, users_max, model_name) VALUES " +
                $"(920001, 'private', 'hk_100% fun', 'desc', {Target}, 'locked', 0, 25, 'model_a'), (920002, 'public', 'hk_1000 fun', '', {Owner}, 'open', 0, 50, 'model_a')");
        var rooms = new HousekeepingRoomStore(_database, NoLoadedRooms());
        var found = Assert.Single(rooms.Search("hk_100%", false, 10));
        Assert.Equal((920001, "hk_100% fun", "hk_o'brien", true, false), (found.Id, found.Name, found.OwnerName, found.IsLocked, found.IsPublic));
        Assert.Equal(2, rooms.Search("hk_10", false, 10).Count);
        Assert.Empty(rooms.Search("hk_10", true, 10));
        Assert.True(rooms.Find(920002)!.IsPublic);
        Assert.Null(rooms.Find(920099));
    }

    [HousekeepingDatabaseFact]
    public void DashboardCountsPeaksAndRecentSanctions()
    {
        Execute("INSERT INTO housekeeping_online_peaks (day, peak) VALUES (UTC_DATE(), 12), (UTC_DATE() - INTERVAL 3 DAY, 40)");
        new HousekeepingAuditLog(_database).Write(Owner, "hk_owner", "user.mute", HousekeepingOutcome.Success(HousekeepingTarget.User(Target), "minutes=5"));
        new ModerationManager(_database, NullLogger<ModerationManager>.Instance).BanUser("hk_owner", ModerationBanType.Username, "hk_peer", "x", PlusEnvironment.GetUnixTimestamp() + 60);
        var lookups = new HousekeepingLookups(_clients, null!, new ModerationManager(_database, NullLogger<ModerationManager>.Instance), NoLoadedRooms(), _database);
        var dashboard = lookups.Dashboard();
        Assert.Equal((12, 40, 2), (dashboard.PeakOnlineToday, dashboard.PeakOnlineAllTime, dashboard.SanctionsLast24h));
        Assert.Equal(Scalar<int>("SELECT COUNT(*) FROM users"), dashboard.TotalUsers);
        Assert.InRange(dashboard.ServerUptimeSeconds, 0, int.MaxValue);
    }

    [HousekeepingDatabaseFact]
    public async Task LoginInFlightKeepsOfflineGrantsOutOfTheStaleWallet()
    {
        var (login, release, session, gate) = StartLogin();
        var economy = new HousekeepingEconomyActions(_users, _clients, null!, null!, null!, _database, gate);
        var grant = Task.Run(() => economy.Give(Staff(), Target, HousekeepingCurrency.Credits, 50));
        await Task.Delay(300);
        Assert.False(grant.IsCompleted);
        release.SetResult();
        Assert.Null(await login);
        Assert.True((await grant).Ok);
        // The grant lands in the live wallet that logout saves, not under it in the row.
        Assert.Equal(150, session.GetHabbo().Credits);
        Assert.Equal(100, Scalar<int>($"SELECT credits FROM users WHERE id = {Target}"));
    }

    [HousekeepingDatabaseFact]
    public async Task PasswordResetDuringLoginLoadClosesTheNewSession()
    {
        var (login, release, session, gate) = StartLogin();
        var disconnected = false;
        session.DisconnectRequested = () => disconnected = true;
        var actions = new HousekeepingUserActions(_users, _clients, null!, null!, null!, new Argon2idPasswordHasher(), _database, gate);
        var reset = Task.Run(() => actions.ResetPassword(Staff(), Target));
        await Task.Delay(300);
        Assert.False(reset.IsCompleted);
        release.SetResult();
        Assert.Null(await login);
        Assert.True((await reset).Ok);
        Assert.True(disconnected);
    }

    [HousekeepingDatabaseFact]
    public async Task PasswordResetAfterTheTicketResolvedRejectsTheLogin()
    {
        var gate = new AccountSessionGate();
        var actions = new HousekeepingUserActions(_users, _clients, null!, null!, null!, new Argon2idPasswordHasher(), _database, gate);
        var authenticator = Authenticator(new SlowLogin(_users, Task.CompletedTask), gate);
        var connections = 0;
        HousekeepingOutcome? reset = null;
        // The second connection is the ticket reset that follows ticket resolution; the staff reset lands right there.
        _database.BeforeConnection = () =>
        {
            if (++connections != 2) return;
            _database.BeforeConnection = null;
            reset = actions.ResetPassword(Staff(), Target);
        };
        var (session, _) = HabbiconTestSupport.Client(null!);
        var result = await authenticator.AuthenticateUsingSSO(session, Ticket);
        Assert.True(reset!.Ok);
        Assert.Equal(AuthenticationError.LoginProhibited, result);
        Assert.Null(_clients.GetClientByUserId(Target));
    }

    private const string Ticket = "hk-login-ticket-000001";

    /// <summary>Starts a real SSO login and returns once it has loaded the account but not yet registered the session.</summary>
    private (Task<AuthenticationError?> Login, TaskCompletionSource Release, GameClient Session, AccountSessionGate Gate) StartLogin()
    {
        var release = new TaskCompletionSource();
        var gate = new AccountSessionGate();
        var factory = new SlowLogin(_users, release.Task);
        var (session, _) = HabbiconTestSupport.Client(null!);
        var login = Authenticator(factory, gate).AuthenticateUsingSSO(session, Ticket);
        Assert.True(factory.Loaded.Task.Wait(TimeSpan.FromSeconds(10)));
        return (login, release, session, gate);
    }

    private Authenticator Authenticator(IUserDataFactory factory, IAccountSessionGate gate)
    {
        // Habbo.Init loads effects and clothing through the static database.
        typeof(PlusEnvironment).GetField("_database", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, _database);
        // Deployed schemas let the SSO ticket be NULL, which Authenticator.ResetSso writes.
        Execute("ALTER TABLE users MODIFY auth_ticket VARCHAR(60) NULL");
        Execute($"UPDATE users SET auth_ticket = '{Ticket}' WHERE id = {Target}");
        return new Authenticator(Array.Empty<IAuthenticationTask>(), _clients, factory, _database, gate);
    }

    /// <summary>Loads the account from the database, then holds the login until released.</summary>
    private sealed class SlowLogin(IHousekeepingUserStore users, Task release) : IUserDataFactory
    {
        public TaskCompletionSource Loaded { get; } = new();

        public async Task<Habbo?> Create(int userId)
        {
            var record = users.Find(userId)!;
            var habbo = new Habbo { Id = record.Id, Username = record.Username, Rank = record.Rank, Credits = record.Credits, Permissions = new(new(), new()) };
            Loaded.TrySetResult();
            await release;
            return habbo;
        }

        public Task<string> GetUsernameForHabboById(int userId) => throw new NotSupportedException();
        public Task<bool> HabboExists(int userId) => throw new NotSupportedException();
        public Task<bool> HabboExists(string username) => throw new NotSupportedException();
        public Task<Habbo?> GetUserDataByIdAsync(int userId) => throw new NotSupportedException();
        public Task<List<Plus.HabboHotel.Users.Badges.Badge>> GetEquippedBadgesForUserAsync(int userId) => throw new NotSupportedException();
    }

    private static IRoomManager NoLoadedRooms() => DispatchProxy.Create<IRoomManager, NoRoomsProxy>();

    public class NoRoomsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IRoomManager.TryGetRoom) => false,
            nameof(IRoomManager.GetRooms) => new List<Room>(),
            _ => throw new NotSupportedException(method?.Name)
        };
    }
}
