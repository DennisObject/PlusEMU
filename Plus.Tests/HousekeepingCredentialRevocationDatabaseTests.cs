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
using Xunit;

namespace Plus.Tests;

// Staff resets, bans and demotions against the HTTP credential paths, on the real stores.
[Collection("HousekeepingDatabase")]
public class HousekeepingCredentialRevocationDatabaseTests
{
    private const int Staff = 940001, Target = 940002, Moderator = 940003, Neighbour = 940004;
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
                "DELETE FROM bans WHERE value LIKE 'cr\\_%' OR value LIKE '10.94.%' OR value = 'cr-machine'");
        var hash = new Argon2idPasswordHasher().Hash(OldPassword);
        Execute("INSERT INTO users (id, username, password, auth_ticket, `rank`, ip_last, online) VALUES " +
                $"({Staff}, 'cr_staff', '', '', 9, '', 0), ({Target}, 'cr_target', @hash, '', 1, '10.94.0.2', 0), " +
                $"({Moderator}, 'cr_moderator', @hash, '', 3, '', 0), ({Neighbour}, 'cr_neighbour', @hash, '', 1, '10.94.0.4', 0)", new { hash });
        Execute($"INSERT INTO user_info (user_id) VALUES ({Target}), ({Moderator}), ({Neighbour})");
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
        Assert.True(Actions().Ban(StaffHabbo(), Target, "cheating", 1).Ok);
        AssertSignedOut(Target);

        await Login().Login("cr_moderator", OldPassword, "10.0.0.1", remember: true);
        Assert.True(Actions().SetRank(StaffHabbo(), Moderator, 4).Ok);
        Assert.Equal(1, LiveAccessTokens(Moderator));
        Assert.True(Actions().SetRank(StaffHabbo(), Moderator, 2).Ok);
        AssertSignedOut(Moderator);
    }

    // :ban, :ipban, :mip, the mod tool and word-filter bans all go through ModerationManager.BanUser.
    [HousekeepingDatabaseFact]
    public async Task EveryBanPathSignsTheBannedAccountsOut()
    {
        await Login().Login("cr_target", OldPassword, "10.0.0.1", remember: true);
        await Login().Login("cr_neighbour", OldPassword, "10.0.0.1", remember: true);
        var moderation = Moderation();
        moderation.BanUser("cr_staff", ModerationBanType.Username, "cr_target", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        AssertSignedOut(Target);
        Assert.Equal(1, LiveAccessTokens(Neighbour));

        // An IP ban reaches the accounts last seen at that address, and no others.
        await Login().Login("cr_moderator", OldPassword, "10.0.0.1");
        moderation.BanUser("cr_staff", ModerationBanType.Ip, "10.94.0.4", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        AssertSignedOut(Neighbour);
        Assert.Equal(1, LiveAccessTokens(Moderator));

        // A machine ban reaches the sessions online with that machine id.
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = Moderator, Username = "cr_moderator" });
#pragma warning disable CS0618
        client.MachineId = "cr-machine";
#pragma warning restore CS0618
        _clients.Online[Moderator] = client;
        moderation.BanUser("cr_staff", ModerationBanType.Machine, "cr-machine", "spam", PlusEnvironment.GetUnixTimestamp() + 3600);
        Assert.Equal(0, LiveAccessTokens(Moderator));
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

    private ModerationManager Moderation(ISessionIssuer? sessions = null) => new(_database, NullLogger<ModerationManager>.Instance, sessions ?? _sessions, _clients);

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
        public async Task RevokeAll(int userId)
        {
            await inner.RevokeAll(userId);
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
