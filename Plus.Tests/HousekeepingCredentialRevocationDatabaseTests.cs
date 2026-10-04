using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Plus.Communication.Http;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities;
using Xunit;

namespace Plus.Tests;

// Staff resets, bans and demotions against the HTTP credential paths, on the real stores.
[Collection("HousekeepingDatabase")]
public class HousekeepingCredentialRevocationDatabaseTests
{
    private const int Staff = 940001, Target = 940002, Moderator = 940003, Neighbour = 940004, Locked = 940005, Unbanned = 940006;
    private const string OldPassword = "old-password-1234";
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly IOptions<AuthApiConfiguration> _options = Options.Create(new AuthApiConfiguration());
    private readonly AccountSessionGate _gate = new();
    private readonly SsoTicketStore _tickets;
    private readonly RememberTokenStore _remember;
    private readonly SessionIssuer _sessions;
    private readonly BoundedPasswordHasher _hasher;
    private readonly HousekeepingActionTests.FakeClients _clients = new();

    public HousekeepingCredentialRevocationDatabaseTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING") ?? "";
        if (connectionString.Length > 0 && !new MySqlConnectionStringBuilder(connectionString).Database.StartsWith("task_housekeeping_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Housekeeping database tests require a disposable task_housekeeping_tests_ schema.");
        _database = new(connectionString);
        _hasher = new(new Argon2idPasswordHasher(), _options);
        _tickets = new(_database, TimeProvider.System, _options);
        _remember = new(_database, TimeProvider.System, _options);
        _sessions = Sessions();
        if (connectionString.Length == 0) return;
        Execute("DELETE FROM users WHERE id BETWEEN 940000 AND 940099; DELETE FROM user_info WHERE user_id BETWEEN 940000 AND 940099; " +
                "DELETE FROM user_access_tokens WHERE user_id BETWEEN 940000 AND 940099; DELETE FROM user_remember_tokens WHERE user_id BETWEEN 940000 AND 940099; " +
                "DELETE FROM bans WHERE value LIKE 'cr\\_%' OR value LIKE 'cr-%' OR value LIKE '10.94.%'");
        var hash = new Argon2idPasswordHasher().Hash(OldPassword);
        Execute("INSERT INTO users (id, username, password, auth_ticket, `rank`, ip_last, online) VALUES " +
                $"({Staff}, 'cr_staff', '', '', 9, '', 0), ({Target}, 'cr_target', @hash, '', 1, '10.94.0.2', 0), " +
                $"({Moderator}, 'cr_moderator', @hash, '', 3, '', 0), ({Neighbour}, 'cr_neighbour', @hash, '', 1, '10.94.0.4', 0), " +
                $"({Locked}, 'cr_locked', @hash, '', 1, '', 0), ({Unbanned}, 'cr_unbanned', @hash, '', 1, '', 0)", new { hash });
        Execute($"INSERT INTO user_info (user_id) VALUES ({Target}), ({Moderator}), ({Neighbour}), ({Locked}), ({Unbanned})");
    }

    [HousekeepingDatabaseFact]
    public async Task ResetAfterAnHttpLoginReadTheOldHashKeepsItFromIssuing()
    {
        HousekeepingOutcome? reset = null;
        // The reset lands after the login has verified the old password and before it issues anything.
        var login = Login(new AfterVerify(_hasher, () => reset = Actions().ResetPassword(StaffHabbo(), Target)));
        var result = await login.Login("cr_target", OldPassword, "10.0.0.1");
        Assert.True(reset!.Ok);
        Assert.NotEqual(LoginStatus.Success, result.Status);
        AssertSignedOut(Target);
        Assert.NotEqual(LoginStatus.Success, (await Login().Login("cr_target", OldPassword, "10.0.0.1")).Status);
        Assert.Equal(LoginStatus.Success, (await Login().Login("cr_target", reset.Message, "10.0.0.1")).Status);
    }

    // E1's rule: the new hash must be written before (or with) the generation bump. A login that reads the row right
    // after the bump captures the new generation, so it must already see the new password.
    [HousekeepingDatabaseFact]
    public async Task AResetWritesTheNewHashBeforeItRevokes()
    {
        LoginResult? midReset = null;
        var sessions = new AfterRevokeAll(_sessions, () => midReset = Login().Login("cr_target", OldPassword, "10.0.0.1").GetAwaiter().GetResult());
        Assert.True(Actions(sessions).ResetPassword(StaffHabbo(), Target).Ok);
        Assert.NotEqual(LoginStatus.Success, midReset!.Status);
        AssertSignedOut(Target);
    }

    [HousekeepingDatabaseFact]
    public async Task ResetRevokesEveryCredentialIssuedBeforeIt()
    {
        var session = (await Login().Login("cr_target", OldPassword, "10.0.0.1", remember: true)).Session!;
        Assert.True(Actions().ResetPassword(StaffHabbo(), Target).Ok);
        AssertSignedOut(Target);
        Assert.Equal(ResumeStatus.Invalid, (await _sessions.Resume(session.RememberToken!.Value.Value, "10.0.0.1", withTicket: true)).Status);
    }

    [HousekeepingDatabaseFact]
    public async Task ResetAfterARememberTokenRotatedKeepsTheResumeFromIssuing()
    {
        var remembered = (await Login().Login("cr_target", OldPassword, "10.0.0.1", remember: true)).Session!.RememberToken!.Value.Value;
        HousekeepingOutcome? reset = null;
        var sessions = Sessions(remember: new AfterRotate(_remember, () => reset = Actions().ResetPassword(StaffHabbo(), Target)));
        var resumed = await sessions.Resume(remembered, "10.0.0.1", withTicket: true);
        Assert.True(reset!.Ok);
        Assert.Equal(ResumeStatus.Invalid, resumed.Status);
        AssertSignedOut(Target);
    }

    [HousekeepingDatabaseFact]
    public async Task ResetAfterATicketExchangeResolvedKeepsTheAccessTokenFromIssuing()
    {
        var ticket = (await Login().Login("cr_target", OldPassword, "10.0.0.1")).Session!.SsoTicket.Value;
        HousekeepingOutcome? reset = null;
        var sessions = Sessions(tickets: new AfterExchange(_tickets, () => reset = Actions().ResetPassword(StaffHabbo(), Target)));
        Assert.Null(await sessions.ExchangeTicket(ticket));
        Assert.True(reset!.Ok);
        AssertSignedOut(Target);
    }

    [HousekeepingDatabaseFact]
    public async Task HousekeepingBansAndDemotionsSignTheAccountOutEverywhere()
    {
        await Login().Login("cr_target", OldPassword, "10.0.0.1", remember: true);
        Assert.True((await Actions().Ban(StaffHabbo(), Target, "cheating", 1)).Ok);
        AssertSignedOut(Target);

        await Login().Login("cr_moderator", OldPassword, "10.0.0.1", remember: true);
        Assert.True(Actions().SetRank(StaffHabbo(), Moderator, 4).Ok);
        Assert.Equal(1, LiveAccessTokens(Moderator));
        Assert.True(Actions().SetRank(StaffHabbo(), Moderator, 2).Ok);
        AssertSignedOut(Moderator);
    }

    // :ban, :ipban, :mip, the mod tool, word-filter bans and housekeeping all go through ModerationManager.BanUser.
    [HousekeepingDatabaseFact]
    public async Task EveryBanPathSignsTheBannedAccountsOutAndClosesTheirSessions()
    {
        await Login().Login("cr_target", OldPassword, "10.94.0.2", remember: true);
        // HTTP logins record the client address in users.ip_last, which IP bans select on.
        await Login().Login("cr_neighbour", OldPassword, "10.94.0.4", remember: true);
        var target = Online(Target, "cr_target");
        var neighbour = Online(Neighbour, "cr_neighbour");
        var moderation = Moderation();
        await moderation.BanUser("cr_staff", ModerationBanType.Username, "cr_target", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        AssertSignedOut(Target);
        Assert.True(target.Closed.IsCancellationRequested);
        Assert.Equal(1, LiveAccessTokens(Neighbour));
        Assert.False(neighbour.Closed.IsCancellationRequested);

        // An IP ban reaches the accounts last seen at that address, and no others.
        await Login().Login("cr_moderator", OldPassword, "10.94.0.3");
        await moderation.BanUser("cr_staff", ModerationBanType.Ip, "10.94.0.4", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        AssertSignedOut(Neighbour);
        Assert.True(neighbour.Closed.IsCancellationRequested);
        Assert.Equal(1, LiveAccessTokens(Moderator));

        // A machine ban reaches the sessions online with that machine id.
        var device = Online(Moderator, "cr_moderator", machineId: "cr-machine");
        await moderation.BanUser("cr_staff", ModerationBanType.Machine, "cr-machine", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        Assert.Equal(0, LiveAccessTokens(Moderator));
        Assert.True(device.Closed.IsCancellationRequested);
    }

    [HousekeepingDatabaseFact]
    public async Task AnIpBanClosesEverySessionItCovers()
    {
        Execute($"UPDATE users SET ip_last = '10.94.0.9' WHERE id IN ({Target}, {Neighbour})");
        var first = Online(Target, "cr_target");
        var second = Online(Neighbour, "cr_neighbour");
        await Moderation().BanUser("cr_staff", ModerationBanType.Ip, "10.94.0.9", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        Assert.True(first.Closed.IsCancellationRequested);
        Assert.True(second.Closed.IsCancellationRequested);
    }

    // A game login paused while loading holds the account gate; the ban waits for it, then closes what it attached.
    [HousekeepingDatabaseFact]
    public async Task ABanDuringAGameLoginClosesTheSessionItAttaches()
    {
        var release = new TaskCompletionSource();
        var factory = new SlowLogin(release.Task);
        var (session, _) = HabbiconTestSupport.Client(null!);
        var login = Authenticator(factory).AuthenticateUsingSSO(session, await Ticket());
        Assert.True(factory.Loaded.Task.Wait(TimeSpan.FromSeconds(10)));
        var ban = Task.Run(() => Moderation().BanUser("cr_staff", ModerationBanType.Username, "cr_target", "spam", PlusEnvironment.GetUnixTimestamp() + 3600));
        await Task.Delay(300);
        release.SetResult();
        Assert.Null(await login);
        await ban.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.Closed.IsCancellationRequested);
        AssertSignedOut(Target);
    }

    // A ban that lands after the login used up its ticket, before it reached the gate, stops the login.
    [HousekeepingDatabaseFact]
    public async Task ABanAfterTheTicketWasConsumedRejectsTheLogin()
    {
        var moderation = Moderation();
        var tickets = new AfterConsume(_tickets, () => moderation.BanUser("cr_staff", ModerationBanType.Username, "cr_target", "spam",
            PlusEnvironment.GetUnixTimestamp() + 3600).GetAwaiter().GetResult());
        var (session, _) = HabbiconTestSupport.Client(null!);
        var result = await Authenticator(new SlowLogin(Task.CompletedTask), tickets).AuthenticateUsingSSO(session, await Ticket());
        Assert.Equal(AuthenticationError.LoginProhibited, result);
        Assert.Null(session.GetHabbo());
        Assert.Null(_clients.GetClientByUserId(Target));
    }

    // Hot callers (chat auto-bans, the mod tool) must not hang on a locked users row: the session closes at once,
    // the call returns within its budget, and the revocation is retried once the row frees up.
    [HousekeepingDatabaseFact]
    public async Task ABanUnderALockedUsersRowFailsClosedAndRevokesLater()
    {
        // Its own account: the background retry may still run after this test ends.
        await Login().Login("cr_locked", OldPassword, "10.0.0.1", remember: true);
        var target = Online(Locked, "cr_locked");
        using var locker = new MySqlConnection(Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING"));
        locker.Open();
        var transaction = locker.BeginTransaction();
        try
        {
            locker.Execute($"SELECT id FROM users WHERE id = {Locked} FOR UPDATE", transaction: transaction);
            var ban = Task.Run(() => Moderation().BanUser("System", ModerationBanType.Username, "cr_locked", "auto-ban", PlusEnvironment.GetUnixTimestamp() + 3600));
            await Task.Delay(500);
            Assert.True(target.Closed.IsCancellationRequested);
            await ban.WaitAsync(TimeSpan.FromSeconds(4.5));
        }
        finally
        {
            transaction.Rollback();
        }
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (LiveAccessTokens(Locked) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        AssertSignedOut(Locked);
    }

    // The mod tool's IP and machine bans used the username as the address and the device; they now use the account's
    // recorded address and the target session's handshake machine id.
    [HousekeepingDatabaseFact]
    public async Task TheModToolBansTheRealAddressAndDevice()
    {
        var (moderator, _) = HabbiconTestSupport.Client(new Habbo { Id = Staff, Username = "cr_staff", Rank = 9, Permissions = new(["mod_soft_ban", "mod_ban_any"], []) });
        Online(Target, "cr_target", machineId: "cr-device-1");
        var handler = new Plus.Communication.Packets.Incoming.Moderation.ModerationBanEvent(_clients, Moderation(), _database);
        await handler.Parse(moderator, HabbiconTestSupport.Incoming(Target, "spam", 2, "", "", true, false));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM bans WHERE bantype = 'ip' AND value = '10.94.0.2'"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM bans WHERE bantype = 'user' AND value = 'cr_target'"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM bans WHERE value = 'cr_target' AND bantype <> 'user'"));

        Online(Target, "cr_target", machineId: "cr-device-1");
        await handler.Parse(moderator, HabbiconTestSupport.Incoming(Target, "spam", 2, "", "", false, true));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM bans WHERE bantype = 'machine' AND value = 'cr-device-1'"));
    }

    // A ban whose sign-out timed out must not later revoke a login made after the account was unbanned.
    [HousekeepingDatabaseFact]
    public async Task AnUnbanStopsTheDelayedSignOutOfThatBan()
    {
        var moderation = Moderation();
        await WithLockedRow($"SELECT id FROM users WHERE id = {Unbanned} FOR UPDATE", async () =>
        {
            await moderation.BanUser("System", ModerationBanType.Username, "cr_unbanned", "auto-ban", UnixTimestamp.GetNow() + 3600)
                .WaitAsync(TimeSpan.FromSeconds(4.5));
            Assert.True(moderation.UnbanUser("cr_unbanned"));
        });
        var fresh = await Login().Login("cr_unbanned", OldPassword, "10.0.0.1");
        Assert.Equal(LoginStatus.Success, fresh.Status);
        // Past the first retry of the timed-out sign-out.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, LiveAccessTokens(Unbanned));
    }

    // One deadline covers the whole compound mod-tool ban (account, address, device), however the database stalls.
    [HousekeepingDatabaseFact]
    public async Task TheModToolReturnsWithinTheBudgetWhenTheTargetRowIsLocked()
    {
        await Login().Login("cr_target", OldPassword, "10.94.0.2", remember: true);
        var target = Online(Target, "cr_target", machineId: "cr-device-2");
        var handler = ModTool();
        await WithLockedRow($"SELECT id FROM users WHERE id = {Target} FOR UPDATE", async () =>
        {
            var started = DateTime.UtcNow;
            await handler.Parse(ModeratorSession(), HabbiconTestSupport.Incoming(Target, "spam", 2, "", "", false, true)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromSeconds(4));
            Assert.True(target.Closed.IsCancellationRequested);
        });
        await Eventually(() => LiveAccessTokens(Target) == 0);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM bans WHERE bantype = 'machine' AND value = 'cr-device-2'"));
    }

    [HousekeepingDatabaseFact]
    public async Task TheModToolReturnsWithinTheBudgetWhenTheBansTableIsLocked()
    {
        await Login().Login("cr_target", OldPassword, "10.94.0.2", remember: true);
        var target = Online(Target, "cr_target");
        var handler = ModTool();
        await WithLockedRow("SELECT id FROM bans FOR UPDATE", async () =>
        {
            var started = DateTime.UtcNow;
            await handler.Parse(ModeratorSession(), HabbiconTestSupport.Incoming(Target, "spam", 2, "", "", false, false)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromSeconds(4));
            Assert.True(target.Closed.IsCancellationRequested);
        });
        // The ban row and the sign-out finish in the background once the table frees up.
        await Eventually(() => Scalar<int>("SELECT COUNT(*) FROM bans WHERE bantype = 'user' AND value = 'cr_target'") == 1);
        await Eventually(() => LiveAccessTokens(Target) == 0);
    }

    private async Task WithLockedRow(string lockSql, Func<Task> whileLocked)
    {
        using var locker = new MySqlConnection(Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING"));
        locker.Open();
        using var transaction = locker.BeginTransaction();
        locker.Execute(lockSql, transaction: transaction);
        try
        {
            await whileLocked();
        }
        finally
        {
            transaction.Rollback();
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        Assert.True(condition());
    }

    private Plus.Communication.Packets.Incoming.Moderation.ModerationBanEvent ModTool() => new(_clients, Moderation(), _database);

    private static Plus.HabboHotel.GameClients.GameClient ModeratorSession() =>
        HabbiconTestSupport.Client(new Habbo { Id = Staff, Username = "cr_staff", Rank = 9, Permissions = new(["mod_soft_ban", "mod_ban_any"], []) }).Client;

    private Plus.HabboHotel.GameClients.GameClient Online(int userId, string username, string machineId = "")
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = userId, Username = username, Rank = 1, Permissions = new([], []) });
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
        client.MachineId = machineId;
#pragma warning restore CS0618
        _clients.Online[userId] = client;
        return client;
    }

    private async Task<string> Ticket() => (await _tickets.Issue(Target)).Value;

    private Authenticator Authenticator(Plus.HabboHotel.Users.UserData.IUserDataFactory factory, ISsoTicketStore? tickets = null)
    {
        // Habbo.Init loads effects and clothing through the static database.
        typeof(PlusEnvironment).GetField("_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, _database);
        return new(Array.Empty<IAuthenticationTask>(), _clients, factory, tickets ?? _tickets, _gate);
    }

    /// <summary>Loads the target from the database, then holds the login until released.</summary>
    private sealed class SlowLogin(Task release) : Plus.HabboHotel.Users.UserData.IUserDataFactory
    {
        public TaskCompletionSource Loaded { get; } = new();

        public async Task<Habbo?> Create(int userId, CancellationToken cancellationToken = default)
        {
            var habbo = new Habbo { Id = userId, Username = "cr_target", Rank = 1, Permissions = new([], []) };
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

    private sealed class AfterConsume(ISsoTicketStore inner, Action hook) : ISsoTicketStore
    {
        public async Task<int?> Consume(string ticket)
        {
            var userId = await inner.Consume(ticket);
            hook();
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

    private void AssertSignedOut(int userId)
    {
        Assert.Equal(0, LiveAccessTokens(userId));
        Assert.Equal(0, Scalar<int>($"SELECT COUNT(*) FROM user_remember_tokens WHERE user_id = {userId} AND revoked_at IS NULL AND used_at IS NULL"));
        Assert.Equal("", Scalar<string>($"SELECT auth_ticket FROM users WHERE id = {userId}"));
    }

    private int LiveAccessTokens(int userId) => Scalar<int>($"SELECT COUNT(*) FROM user_access_tokens WHERE user_id = {userId} AND revoked_at IS NULL");

    private SessionIssuer Sessions(ISsoTicketStore? tickets = null, IRememberTokenStore? remember = null) =>
        new(tickets ?? _tickets, new AccessTokenStore(_database, TimeProvider.System, _options), remember ?? _remember, new CredentialGenerations(_database),
            new AccountStore(_database, TimeProvider.System, _options), new BanLookup(_database, TimeProvider.System));

    private LoginService Login(IBoundedPasswordHasher? hasher = null) =>
        new(new AccountStore(_database, TimeProvider.System, _options), hasher ?? _hasher, new LoginThrottle(TimeProvider.System, _options), _sessions,
            new BanLookup(_database, TimeProvider.System));

    private ModerationManager Moderation(ISessionIssuer? sessions = null) => new(_database, NullLogger<ModerationManager>.Instance, sessions ?? _sessions, _clients, _gate);

    private HousekeepingUserActions Actions(ISessionIssuer? sessions = null)
    {
        var permissions = new PermissionManager(_database, NullLogger<PermissionManager>.Instance);
        permissions.Init();
        return new(new HousekeepingUserStore(_database), _clients, Moderation(sessions), permissions, null!, _hasher, _database, _gate, sessions ?? _sessions);
    }

    private static Habbo StaffHabbo() => new() { Id = Staff, Username = "cr_staff", Rank = 9, Permissions = new(new(), new()) };

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

    private sealed class AfterVerify(IBoundedPasswordHasher inner, Action hook) : IBoundedPasswordHasher
    {
        public Task<string> Hash(string password, CancellationToken cancellationToken = default) => inner.Hash(password, cancellationToken);

        public async Task<PasswordVerificationResult> Verify(string password, string stored, CancellationToken cancellationToken = default)
        {
            var result = await inner.Verify(password, stored, cancellationToken);
            hook();
            return result;
        }
    }

    private sealed class AfterRotate(IRememberTokenStore inner, Action hook) : IRememberTokenStore
    {
        public async Task<RememberRotation> Rotate(string token, Func<int, CredentialScope, Task>? onReuse = null)
        {
            var rotation = await inner.Rotate(token, onReuse);
            hook();
            return rotation;
        }

        public Task<IssuedToken> Issue(int userId, CredentialScope? scope = null) => inner.Issue(userId, scope);
        public Task<IssuedToken> Continue(int userId, string familyId, CredentialScope? scope = null) => inner.Continue(userId, familyId, scope);
        public Task<CredentialOwner?> FindOwner(string token) => inner.FindOwner(token);
        public Task RevokeSession(string sessionId, CredentialScope scope) => inner.RevokeSession(sessionId, scope);
        public Task RevokeAll(int userId, CredentialScope? scope = null) => inner.RevokeAll(userId, scope);
        public Task<int> Prune(long cutoff, int batch) => inner.Prune(cutoff, batch);
    }

    private sealed class AfterExchange(ISsoTicketStore inner, Action hook) : ISsoTicketStore
    {
        public async Task<CredentialOwner?> Exchange(string ticket)
        {
            var owner = await inner.Exchange(ticket);
            hook();
            return owner;
        }

        public Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null) => inner.Issue(userId, sessionId, scope);
        public Task<int?> FindUser(string ticket) => inner.FindUser(ticket);
        public Task<CredentialOwner?> FindOwner(string ticket) => inner.FindOwner(ticket);
        public Task<CredentialOwner?> Withdraw(int userId, string ticket, CredentialScope scope) => inner.Withdraw(userId, ticket, scope);
        public Task<int?> Consume(string ticket) => inner.Consume(ticket);
        public Task Revoke(int userId, CredentialScope? scope = null) => inner.Revoke(userId, scope);
        public Task RevokeSession(int userId, string sessionId, CredentialScope scope) => inner.RevokeSession(userId, sessionId, scope);
    }

    /// <summary>Runs a hook as soon as the revocation (and its generation bump) has committed.</summary>
    private sealed class AfterRevokeAll(ISessionIssuer inner, Action hook) : ISessionIssuer
    {
        public async Task RevokeAll(int userId, CancellationToken cancellationToken = default)
        {
            await inner.RevokeAll(userId, cancellationToken);
            hook();
        }

        public Task<long> Generation(int userId) => inner.Generation(userId);
        public Task<AuthSession?> Issue(int userId, string username, long generation, string address, bool remember = false) =>
            inner.Issue(userId, username, generation, address, remember);
        public Task Logout(string? accessToken, string? ssoTicket, string? rememberToken) => inner.Logout(accessToken, ssoTicket, rememberToken);
        public Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket) => inner.Resume(rememberToken, address, withTicket);
        public Task<IssuedToken?> ExchangeTicket(string ticket) => inner.ExchangeTicket(ticket);
    }
}
