using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using MySqlConnector;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.Communication.Attributes;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.UserData;
using Plus.Utilities;
using Xunit;

namespace Plus.Tests;

// These tests swap process-wide state (the static database) and lock shared rows, so they never run alongside others.
[CollectionDefinition("HousekeepingDatabase", DisableParallelization = true)]
public sealed class HousekeepingDatabaseCollection;

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
public class HousekeepingDatabaseTests : IDisposable
{
    // Habbo.Init reads the static database; it is swapped in for login tests and put back afterwards.
    private static readonly System.Reflection.FieldInfo StaticDatabase =
        typeof(PlusEnvironment).GetField("_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    private readonly object? _originalStaticDatabase = StaticDatabase.GetValue(null);

    public void Dispose()
    {
        _permissions.Dispose();
        StaticDatabase.SetValue(null, _originalStaticDatabase);
    }

    private const int Owner = 920001, Target = 920002, Peer = 920003;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly HousekeepingUserStore _users;
    private readonly AccessControl _permissions;
    private readonly HousekeepingActionTests.FakeClients _clients = new();

    public HousekeepingDatabaseTests()
    {
        SqlMapper.AddTypeHandler(new Plus.Database.UtcDateTimeOffsetHandler());
        var connectionString = Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING")!;
        if (!new MySqlConnectionStringBuilder(connectionString).Database.StartsWith("task_housekeeping_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Housekeeping database tests require a disposable task_housekeeping_tests_ schema.");
        _database = new(connectionString);
        Execute("DELETE FROM user_roles WHERE user_id BETWEEN 920000 AND 920099; DELETE FROM users WHERE id BETWEEN 920000 AND 920099; DELETE FROM user_info WHERE user_id BETWEEN 920000 AND 920099; " +
                "DELETE FROM rooms WHERE id BETWEEN 920000 AND 920099; DELETE FROM bans; DELETE FROM housekeeping_log; " +
                "DELETE FROM housekeeping_online_peaks; DELETE FROM user_club_memberships WHERE user_id BETWEEN 920000 AND 920099");
        Execute("INSERT INTO users (id, username, auth_ticket, `rank`, credits, activity_points, vip_points, mail, ip_last, online) VALUES " +
                $"({Owner}, 'hk_owner', '', 9, 0, 0, 0, 'owner@hotel', '10.0.0.1', 0), ({Target}, 'hk_o''brien', 'old-ticket', 1, 100, 50, 5, 'target@hotel', '10.0.0.2', 0), " +
                $"({Peer}, 'hk_peer', '', 9, 0, 0, 0, '', '', 1)");
        Execute($"INSERT INTO user_info (user_id, trading_locked) VALUES ({Target}, NULL)");
        Execute($"INSERT INTO user_roles (user_id, role_id) VALUES ({Owner}, 9), ({Target}, 1), ({Peer}, 9)");
        _users = new(_database);
        _permissions = new(_database, _clients, NullLogger<AccessControl>.Instance, TimeProvider.System);
        _permissions.Init();
    }

    private ITradingLockService TradeLocks(IAccountSessionGate? gate = null) => new TradingLockService(_database, _clients, gate ?? new AccountSessionGate(), TimeProvider.System);

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

    private static readonly IOptions<AuthApiConfiguration> AuthOptions = Options.Create(new AuthApiConfiguration());
    private static readonly BoundedPasswordHasher Hasher = new(new Argon2idPasswordHasher(), AuthOptions);

    private SessionIssuer Sessions() =>
        new(new SsoTicketStore(_database, TimeProvider.System, AuthOptions), new AccessTokenStore(_database, TimeProvider.System, AuthOptions),
            new RememberTokenStore(_database, TimeProvider.System, AuthOptions), new CredentialGenerations(_database),
            new AccountStore(_database, TimeProvider.System, AuthOptions), new BanLookup(_database, TimeProvider.System));

    private static Habbo Staff(int rank = 9) => new() { Id = Owner, Username = "hk_owner", Access = HousekeepingPolicyTests.Access(rank * 10, PermissionKeys.HousekeepingEconomy, PermissionKeys.HousekeepingRolesManage) };

    [HousekeepingDatabaseFact]
    public void MigratedHousekeepingRolesMatchTheirModerationBanGrant()
    {
        var groups = Scalar<string>("SELECT GROUP_CONCAT(role_id ORDER BY role_id) FROM role_permissions WHERE permission_key = 'housekeeping.economy'");
        var anchors = Scalar<string>("SELECT GROUP_CONCAT(role_id ORDER BY role_id) FROM role_permissions WHERE permission_key = 'moderation.ban'");
        Assert.Equal(anchors, groups);
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM role_permissions rp LEFT JOIN roles r ON r.id = rp.role_id WHERE r.id IS NULL"));
    }

    [HousekeepingDatabaseFact]
    public void PermissionGroupsExposeTheirRealNameAndBadge()
    {
        Assert.True(_permissions.TryGetRole(9, out var owner));
        Assert.Equal(("Owner", "OWNR"), (owner.Name, owner.BadgeCode));
    }

    [HousekeepingDatabaseFact]
    public void UserLookupsReadAccountRowsByIdAndName()
    {
        var user = _users.Find("hk_o'brien")!;
        Assert.Equal((Target, 100, 50, 5, "target@hotel", "10.0.0.2"), (user.Id, user.Credits, user.Duckets, user.Diamonds, user.Mail, user.IpLast));
        Assert.Equal(9, _permissions.Resolve(Owner).PrimaryRole!.Id);
        Assert.Null(_users.Find(920099));
    }

    [HousekeepingDatabaseFact]
    public void ChatStyleMetadataLoadsAllPickerRowsAndEnforcesItsPermission()
    {
        var styles = new ChatStyleManager(NullLogger<ChatStyleManager>.Instance, _database);
        styles.Init();
        for (var id = 0; id <= 53; id++) Assert.True(styles.TryGetStyle(id, out _));
        Assert.True(styles.TryGetStyle(0, out var normal));
        Assert.True(normal.CanUse(UserAccess.Empty));
        Assert.True(styles.TryGetStyle(9, out var club));
        Assert.True(club.RequiresHc);
        Assert.False(club.CanUse(UserAccess.Empty));
        Assert.True(club.CanUse(UserAccess.Create([], [new(PermissionKeys.ClubAccess, false)])));
        Assert.True(styles.TryGetStyle(34, out var staff));
        Assert.Equal(PermissionKeys.ChatStyleStaff, staff.RequiredPermission);
        Assert.False(staff.CanUse(UserAccess.Empty));
        Assert.True(staff.CanUse(UserAccess.Create([], [new(PermissionKeys.ChatStyleStaff, false)])));
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
        var economy = new HousekeepingEconomyActions(_users, _clients, null!, null!, null!, _database, new AccountSessionGate(), _permissions);
        Assert.True(economy.Give(Staff(), Target, HousekeepingCurrency.Duckets, 25).Ok);
        Assert.Equal(75, Scalar<int>($"SELECT activity_points FROM users WHERE id = {Target}"));
        Execute($"UPDATE users SET vip_points = {int.MaxValue - 10} WHERE id = {Target}");
        Assert.Equal(HousekeepingErrors.EconomyFailed, economy.Give(Staff(), Target, HousekeepingCurrency.Diamonds, 11).Message);
        Assert.Equal(int.MaxValue - 10, Scalar<int>($"SELECT vip_points FROM users WHERE id = {Target}"));
    }

    [HousekeepingDatabaseFact]
    public void ClubGrantsExtendRunningMembershipsAndZeroEndsThem()
    {
        var clubs = new ClubMembershipService(_database, _permissions, TimeProvider.System);
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Execute("INSERT INTO user_club_memberships (user_id, expires_at) VALUES (@Target, @expires)", new { Target, expires = now + 86400 });
        Assert.InRange(clubs.Grant(Staff(), Target, 2)!.Value, now + 86400 * 3, now + 86400 * 3 + 5);
        Assert.InRange(clubs.Grant(Staff(), Target, 0)!.Value, now, now + 5);
        Assert.Null(clubs.Grant(Staff(), Owner, 1));
    }

    [HousekeepingDatabaseFact]
    public async Task BansUseParametersAndUnbanRemovesTheRow()
    {
        var moderation = new ModerationManager(_database, NullLogger<ModerationManager>.Instance, Sessions(), _clients, new AccountSessionGate());
        await moderation.BanUser("hk_owner", ModerationBanType.Username, "hk_o'brien", "it's spam", UnixTimestamp.GetNow() + 3600);
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
        var actions = new HousekeepingUserActions(_users, _clients, null!, _permissions, Hasher, _database, new AccountSessionGate(), Sessions(), TradeLocks());
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
        var actions = new HousekeepingUserActions(_users, _clients, null!, _permissions, null!, _database, new AccountSessionGate(), null!, TradeLocks());
        Assert.True(actions.Mute(Staff(), Target, "", 15).Ok);
        Assert.Equal(900, Scalar<double>($"SELECT time_muted FROM users WHERE id = {Target}"));
        Assert.True(actions.TradeLock(Staff(), Target, 2, "").Ok);
        Assert.True(Scalar<DateTime>($"SELECT trading_locked FROM user_info WHERE user_id = {Target}") > DateTime.UtcNow.AddSeconds(7000));
        Assert.Equal(1, Scalar<int>($"SELECT trading_locks_count FROM user_info WHERE user_id = {Target}"));
        var record = _users.Find(Target)!;
        Assert.True(record.TimeMuted > 0 && record.TradingLockExpiresAt > DateTimeOffset.UtcNow);
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
        new ModerationManager(_database, NullLogger<ModerationManager>.Instance, Sessions(), _clients, new AccountSessionGate()).BanUser("hk_owner", ModerationBanType.Username, "hk_peer", "x", PlusEnvironment.GetUnixTimestamp() + 60).GetAwaiter().GetResult();
        var lookups = new HousekeepingLookups(_clients, null!, new ModerationManager(_database, NullLogger<ModerationManager>.Instance, Sessions(), _clients, new AccountSessionGate()), NoLoadedRooms(), _database);
        var dashboard = lookups.Dashboard();
        Assert.Equal((12, 40, 2), (dashboard.PeakOnlineToday, dashboard.PeakOnlineAllTime, dashboard.SanctionsLast24h));
        Assert.Equal(Scalar<int>("SELECT COUNT(*) FROM users"), dashboard.TotalUsers);
        Assert.InRange(dashboard.ServerUptimeSeconds, 0, int.MaxValue);
    }

    [HousekeepingDatabaseFact]
    public async Task LoginInFlightKeepsOfflineGrantsOutOfTheStaleWallet()
    {
        var (login, release, session, gate) = StartLogin();
        var economy = new HousekeepingEconomyActions(_users, _clients, null!, null!, null!, _database, gate, _permissions);
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
        var actions = new HousekeepingUserActions(_users, _clients, null!, _permissions, Hasher, _database, gate, Sessions(), TradeLocks(gate));
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
        var actions = new HousekeepingUserActions(_users, _clients, null!, _permissions, Hasher, _database, gate, Sessions(), TradeLocks(gate));
        HousekeepingOutcome? reset = null;
        // The staff reset lands right after the login has used up its ticket, before it reaches the gate.
        var authenticator = Authenticator(new SlowLogin(_users, Task.CompletedTask), gate, afterConsume: () => reset = actions.ResetPassword(Staff(), Target));
        var (session, _) = HabbiconTestSupport.Client(null!);
        var result = await authenticator.AuthenticateUsingSSO(session, _ticket);
        Assert.True(reset!.Ok);
        Assert.Equal(AuthenticationError.LoginProhibited, result);
        Assert.Null(_clients.GetClientByUserId(Target));
    }

    [HousekeepingDatabaseFact]
    public async Task LoginWhoseSessionClosesWhileWaitingNeverRegisters()
    {
        var gate = new AccountSessionGate();
        var authenticator = Authenticator(new SlowLogin(_users, Task.CompletedTask), gate);
        var (session, _) = HabbiconTestSupport.Client(null!);
        var held = gate.Enter(Target);
        var login = authenticator.AuthenticateUsingSSO(session, _ticket);
        await Task.Delay(300);
        session.Disconnect();
        held.Dispose();
        Assert.Equal(AuthenticationError.SessionClosed, await login);
        Assert.Null(_clients.GetClientByUserId(Target));
        Assert.Null(session.GetHabbo());
    }

    // Astra's probe: the packet manager gives up on a slow SSO packet after 5 s and disconnects the socket,
    // but the login task keeps running and must not register the dead session once the gate frees up.
    [HousekeepingDatabaseFact]
    public async Task TimedOutSsoPacketCannotRegisterTheSessionLater()
    {
        var gate = new AccountSessionGate();
        var authenticator = Authenticator(new SlowLogin(_users, Task.CompletedTask), gate);
        var handler = new SSOTicketEvent(authenticator, _ticket);
        var (session, _) = HabbiconTestSupport.Client(null!);
        var disconnected = false;
        session.DisconnectRequested = () =>
        {
            disconnected = true;
            session.OnDisconnected();
        };
        using var manager = new PacketManager([handler], NullLogger<PacketManager>.Instance);
        var held = gate.Enter(Target);
        await manager.TryExecutePacket(session, ClientPacketHeader.SSOTicketEvent, new FlashIncomingPacket { Buffer = Array.Empty<byte>() });
        Assert.True(disconnected);
        held.Dispose();
        Assert.Equal(AuthenticationError.SessionClosed, await handler.Attempt!);
        Assert.Null(_clients.GetClientByUserId(Target));
        Assert.Null(session.GetHabbo());
    }

    [NoAuthenticationRequired]
    private sealed class SSOTicketEvent(IAuthenticator authenticator, string ticket) : IPacketEvent
    {
        public Task<AuthenticationError?>? Attempt { get; private set; }
        public Task Parse(GameClient session, IIncomingPacket packet) => Attempt = authenticator.AuthenticateUsingSSO(session, ticket);
    }

    private string _ticket = "";

    /// <summary>Starts a real SSO login and returns once it has loaded the account but not yet registered the session.</summary>
    private (Task<AuthenticationError?> Login, TaskCompletionSource Release, GameClient Session, AccountSessionGate Gate) StartLogin()
    {
        var release = new TaskCompletionSource();
        var gate = new AccountSessionGate();
        var factory = new SlowLogin(_users, release.Task);
        var (session, _) = HabbiconTestSupport.Client(null!);
        var login = Authenticator(factory, gate).AuthenticateUsingSSO(session, _ticket);
        Assert.True(factory.Loaded.Task.Wait(TimeSpan.FromSeconds(10)));
        return (login, release, session, gate);
    }

    private Authenticator Authenticator(IUserDataFactory factory, IAccountSessionGate gate, Action? afterConsume = null)
    {
        // Habbo.Init loads effects and clothing through the static database.
        StaticDatabase.SetValue(null, _database);
        var tickets = new SsoTicketStore(_database, TimeProvider.System, Options.Create(new AuthApiConfiguration()));
        _ticket = tickets.Issue(Target).GetAwaiter().GetResult().Value;
        return new Authenticator(Array.Empty<IAuthenticationTask>(), _clients, factory, new AfterConsume(tickets, afterConsume), gate);
    }

    /// <summary>The real ticket store, with a hook that runs once a ticket has been used up.</summary>
    private sealed class AfterConsume(ISsoTicketStore inner, Action? hook) : ISsoTicketStore
    {
        public async Task<int?> Consume(string ticket)
        {
            var userId = await inner.Consume(ticket);
            hook?.Invoke();
            return userId;
        }

        public Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null) => inner.Issue(userId, sessionId, scope);
        public Task<int?> FindUser(string ticket) => inner.FindUser(ticket);
        public Task<CredentialOwner?> FindOwner(string ticket) => inner.FindOwner(ticket);
        public Task<CredentialOwner?> Exchange(string ticket) => inner.Exchange(ticket);
        public Task<CredentialOwner?> Withdraw(int userId, string ticket, CredentialScope scope) => inner.Withdraw(userId, ticket, scope);
        public Task Revoke(int userId, CredentialScope? scope = null) => inner.Revoke(userId, scope);
        public Task RevokeSession(int userId, string sessionId, CredentialScope scope) => inner.RevokeSession(userId, sessionId, scope);
    }

    /// <summary>Loads the account from the database, then holds the login until released.</summary>
    private sealed class SlowLogin(IHousekeepingUserStore users, Task release) : IUserDataFactory
    {
        public TaskCompletionSource Loaded { get; } = new();

        public async Task<Habbo?> Create(int userId, CancellationToken cancellationToken = default)
        {
            var record = users.Find(userId)!;
            var habbo = new Habbo { Id = record.Id, Username = record.Username, Credits = record.Credits, Access = UserAccess.Empty };
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
