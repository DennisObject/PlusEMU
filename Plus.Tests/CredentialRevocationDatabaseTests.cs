using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

/// <summary>RevokeAll (password reset, ban, theft) must leave no live credential, even when it lands
/// in the middle of a login that already passed its checks.</summary>
[Collection(AuthDatabaseFactAttribute.Collection)]
public sealed class CredentialRevocationDatabaseTests : IDisposable
{
    private readonly List<int> _users = [];
    private readonly AuthTestDatabase _database = new();
    private readonly SsoTicketStore _tickets;
    private readonly AccessTokenStore _access;
    private readonly RememberTokenStore _remember;
    private readonly CredentialGenerations _generations;
    private readonly AccountStore _accounts;

    public CredentialRevocationDatabaseTests()
    {
        var options = AuthTestConfig.Options();
        _tickets = new(_database, TimeProvider.System, options);
        _access = new(_database, TimeProvider.System, options);
        // Strict reuse detection here; RememberGraceDatabaseTests covers the retry window.
        _remember = new(_database, TimeProvider.System, AuthTestConfig.Options(c => c.RememberReuseGraceSeconds = 0));
        _generations = new(_database, TimeProvider.System);
        _accounts = new(_database, TimeProvider.System, options);
    }

    private SessionIssuer Issuer(IAccountStore? accounts = null, IBanLookup? bans = null) =>
        new(_tickets, _access, _remember, _generations, accounts ?? _accounts, bans ?? new BanLookup(_database, TimeProvider.System), TimeProvider.System);

    [AuthDatabaseFact]
    public async Task RevokeAllLandingMidResumeLeavesNothingLive()
    {
        var userId = User();
        var paused = new PausedAccounts(_accounts);
        var issuer = Issuer(accounts: paused);
        var token = await _remember.Issue(userId);

        var pending = issuer.Resume(token.Value, "203.0.113.8", withTicket: true);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await issuer.RevokeAll(userId);
        paused.Release.SetResult();
        var result = await pending;

        Assert.Equal(ResumeStatus.Invalid, result.Status);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task RevokeAllLandingMidPasswordLoginLeavesNothingLive()
    {
        var hasher = new Argon2idPasswordHasher();
        var userId = User(hasher.Hash("correct horse"));
        var paused = new PausedBans();
        var issuer = Issuer(bans: paused);
        var login = new LoginService(_accounts, new BoundedPasswordHasher(hasher, AuthTestConfig.Options()),
            new LoginThrottle(TimeProvider.System, AuthTestConfig.Options()), issuer, paused);

        var pending = login.Login(await Name(userId), "correct horse", "203.0.113.8", remember: true);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await issuer.RevokeAll(userId);
        paused.Release.SetResult();
        var result = await pending;

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task RevokeAllLandingMidTicketExchangeLeavesNoAccessToken()
    {
        var userId = User();
        var issuer = Issuer();
        var session = await issuer.Issue(userId, "x", await issuer.Generation(userId), "203.0.113.8");
        var paused = new PausedTickets(_tickets);
        var racing = new SessionIssuer(paused, _access, _remember, _generations, _accounts, new BanLookup(_database, TimeProvider.System), TimeProvider.System);

        var pending = racing.ExchangeTicket(session!.SsoTicket.Value);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await issuer.RevokeAll(userId);
        paused.Release.SetResult();

        Assert.Null(await pending);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task ATicketExchangesOnceForAnAccessToken()
    {
        var userId = User();
        var issuer = Issuer();
        var session = await issuer.Issue(userId, "x", await issuer.Generation(userId), "203.0.113.8");

        var token = await issuer.ExchangeTicket(session!.SsoTicket.Value);

        Assert.Equal(userId, await _access.FindUser(token!.Value.Value));
        Assert.Null(await issuer.ExchangeTicket(session.SsoTicket.Value));
    }

    [AuthDatabaseFact]
    public async Task ThePasswordAndItsGenerationAreReadInOneSnapshot()
    {
        var userId = User("secret");
        using (var connection = new MySqlConnection(AuthTestDatabase.ConnectionString))
            connection.Execute("UPDATE users SET credential_generation = 5 WHERE id = @userId", new { userId });

        var account = await _accounts.FindByUsername(await Name(userId));

        Assert.Equal(new AccountCredentials(userId, await Name(userId), "secret", 5), account);
    }

    [AuthDatabaseFact]
    public async Task LogoutLandingMidResumeVoidsThatDevicesSession()
    {
        var userId = User();
        var device = (await Issuer().Issue(userId, "x", await _generations.Current(userId), "203.0.113.8", remember: true))!;
        var paused = new PausedAccounts(_accounts);
        var racing = Issuer(accounts: paused);

        var pending = racing.Resume(device.RememberToken!.Value.Value, "203.0.113.8", withTicket: true);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Issuer().Logout(device.AccessToken.Value, device.SsoTicket.Value, device.RememberToken!.Value.Value);
        paused.Release.SetResult();

        Assert.Equal(ResumeStatus.Invalid, (await pending).Status);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task LogoutLandingMidTicketExchangeVoidsTheExchange()
    {
        var userId = User();
        var device = (await Issuer().Issue(userId, "x", await _generations.Current(userId), "203.0.113.8"))!;
        var paused = new PausedTickets(_tickets);
        var racing = new SessionIssuer(paused, _access, _remember, _generations, _accounts, new BanLookup(_database, TimeProvider.System), TimeProvider.System);

        var pending = racing.ExchangeTicket(device.SsoTicket.Value);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Issuer().Logout(null, device.SsoTicket.Value, null);
        paused.Release.SetResult();

        Assert.Null(await pending);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task LogoutLandingMidExchangeOfASessionlessTicketVoidsTheExchange()
    {
        var userId = User();
        var ticket = await SessionlessTicket(userId);
        var paused = new PausedTickets(_tickets);
        var racing = new SessionIssuer(paused, _access, _remember, _generations, _accounts, new BanLookup(_database, TimeProvider.System), TimeProvider.System);

        var pending = racing.ExchangeTicket(ticket);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Issuer().Logout(null, ticket, null);
        paused.Release.SetResult();

        Assert.Null(await pending);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task LogoutRacingTheExchangeOfASessionlessTicketNeverLeavesALiveBearer()
    {
        var userId = User();
        var issuer = Issuer();
        for (var round = 0; round < 200; round++)
        {
            var ticket = await SessionlessTicket(userId);

            var exchange = Task.Run(() => issuer.ExchangeTicket(ticket));
            var logout = Task.Run(() => issuer.Logout(null, ticket, null));
            await Task.WhenAll(exchange, logout);

            await AssertNothingLive(userId);
        }
    }

    [AuthDatabaseFact]
    public async Task ExchangingASessionlessTicketGivesItASession()
    {
        var userId = User();
        var ticket = await SessionlessTicket(userId);

        var owner = await _tickets.Exchange(ticket);

        Assert.NotNull(owner?.SessionId);
        Assert.Equal(owner, await _tickets.FindOwner(ticket));
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM user_sessions WHERE id = @SessionId AND user_id = @UserId AND revoked_at IS NULL", owner));
    }

    [AuthDatabaseFact]
    public async Task SessionsHeldOnlyByALiveTicketSurvivePruning()
    {
        var userId = User();
        var session = (await Issuer().Issue(userId, "x", await _generations.Current(userId), "203.0.113.8"))!;
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        connection.Execute("DELETE FROM user_access_tokens WHERE user_id = @userId; UPDATE user_sessions SET created_at = '1970-01-01 00:00:01' WHERE user_id = @userId", new { userId });
        var cutoff = DateTimeOffset.UtcNow;

        await _generations.PruneSessions(cutoff, 100);
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_sessions WHERE user_id = @userId", new { userId }));
        Assert.Equal(userId, (await _tickets.Exchange(session.SsoTicket.Value))?.UserId);

        connection.Execute("UPDATE users SET auth_ticket_expires_at = '1970-01-01 00:00:01' WHERE id = @userId", new { userId });
        await _generations.PruneSessions(cutoff, 100);
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_sessions WHERE user_id = @userId", new { userId }));
    }

    /// <summary>A ticket written the way a CMS does it: no login session behind it.</summary>
    private static async Task<string> SessionlessTicket(int userId)
    {
        var ticket = SecureToken.Generate();
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        await connection.ExecuteAsync("UPDATE users SET auth_ticket = @ticket, auth_ticket_expires_at = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 300 SECOND), auth_ticket_exchanged = 0, auth_ticket_session = NULL WHERE id = @userId",
            new { ticket, userId });
        return ticket;
    }

    [AuthDatabaseFact]
    public async Task LogoutSignsOutOnlyThatDevice()
    {
        var userId = User();
        var issuer = Issuer();
        var phone = (await issuer.Issue(userId, "x", await _generations.Current(userId), "203.0.113.8", remember: true))!;
        var laptop = (await issuer.Issue(userId, "x", await _generations.Current(userId), "203.0.113.9", remember: true))!;

        await issuer.Logout(phone.AccessToken.Value, null, null);

        Assert.Null(await _access.FindUser(phone.AccessToken.Value));
        Assert.Equal(RememberRotationStatus.Invalid, (await _remember.Rotate(phone.RememberToken!.Value.Value)).Status);
        Assert.Equal(userId, await _access.FindUser(laptop.AccessToken.Value));
        Assert.Equal(userId, await _tickets.FindUser(laptop.SsoTicket.Value));
        Assert.Equal(ResumeStatus.Resumed, (await issuer.Resume(laptop.RememberToken!.Value.Value, "203.0.113.9", withTicket: false)).Status);
    }

    [AuthDatabaseFact]
    public async Task LogoutWithOnlyTheRememberTokenEndsThatSessionsAccessToo()
    {
        var userId = User();
        var issuer = Issuer();
        var device = (await issuer.Issue(userId, "x", await _generations.Current(userId), "203.0.113.8", remember: true))!;

        await issuer.Logout(null, null, device.RememberToken!.Value.Value);

        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task ReplayingAnAlreadyRevokedTokenHasNoSideEffects()
    {
        var userId = User();
        var issuer = Issuer();
        var first = (await issuer.Issue(userId, "x", await _generations.Current(userId), "203.0.113.8", remember: true))!.RememberToken!.Value.Value;
        await issuer.Resume(first, "203.0.113.8", withTicket: false);
        Assert.Equal(ResumeStatus.Invalid, (await issuer.Resume(first, "203.0.113.8", withTicket: false)).Status);
        var afterTheft = await _generations.Current(userId);
        var recovered = (await issuer.Issue(userId, "x", afterTheft, "203.0.113.8", remember: true))!;

        Assert.Equal(ResumeStatus.Invalid, (await issuer.Resume(first, "203.0.113.8", withTicket: false)).Status);

        Assert.Equal(afterTheft, await _generations.Current(userId));
        Assert.Equal(userId, await _access.FindUser(recovered.AccessToken.Value));
        Assert.Equal(userId, await _tickets.FindUser(recovered.SsoTicket.Value));
    }

    [AuthDatabaseFact]
    public async Task FirstReuseDetectionCommitsNothingUnlessTheAccountRevokeSucceeds()
    {
        var userId = User();
        var token = await _remember.Issue(userId);
        var rotation = await _remember.Rotate(token.Value);
        await _remember.Continue(userId, rotation.FamilyId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _remember.Rotate(token.Value, (_, _) => throw new InvalidOperationException("crash")));
        var before = await _generations.Current(userId);
        var reuse = await _remember.Rotate(token.Value, async (id, scope) => await _generations.Bump(id, scope));

        Assert.Equal(RememberRotationStatus.Reused, reuse.Status);
        Assert.Equal(before + 1, await _generations.Current(userId));
    }

    [AuthDatabaseFact]
    public async Task LoginsAndResumesRecordTheClientAddress()
    {
        var userId = User();
        var issuer = Issuer();
        var device = (await issuer.Issue(userId, "x", await _generations.Current(userId), "203.0.113.8", remember: true))!;
        Assert.Equal("203.0.113.8", await LastAddress(userId));

        await issuer.Resume(device.RememberToken!.Value.Value, "198.51.100.4", withTicket: false);

        Assert.Equal("198.51.100.4", await LastAddress(userId));
    }

    private static async Task<string> LastAddress(int userId)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        return await connection.QuerySingleAsync<string>("SELECT ip_last FROM users WHERE id = @userId", new { userId });
    }

    [AuthDatabaseFact]
    public async Task ReplayingARotatedRememberTokenSignsTheUserOutEverywhere()
    {
        var userId = User();
        var issuer = Issuer();
        var first = await _remember.Issue(userId);
        var thief = await issuer.Resume(first.Value, "203.0.113.8", withTicket: true);

        var replay = await issuer.Resume(first.Value, "203.0.113.9", withTicket: true);

        Assert.Equal(ResumeStatus.Resumed, thief.Status);
        Assert.Equal(ResumeStatus.Invalid, replay.Status);
        Assert.Null(await _access.FindUser(thief.Session!.AccessToken.Value));
        Assert.Null(await _tickets.FindUser(thief.Session.SsoTicket.Value));
        Assert.Equal(RememberRotationStatus.Invalid, (await _remember.Rotate(thief.Session.RememberToken!.Value.Value)).Status);
    }

    [AuthDatabaseFact]
    public async Task RevokeAllBumpsTheGenerationAndIssueRefusesAStaleOne()
    {
        var userId = User();
        var issuer = Issuer();
        var before = await issuer.Generation(userId);

        await issuer.RevokeAll(userId);

        Assert.Equal(before + 1, await issuer.Generation(userId));
        Assert.Null(await issuer.Issue(userId, "x", before, "203.0.113.8", remember: true));
        Assert.NotNull(await issuer.Issue(userId, "x", before + 1, "203.0.113.8", remember: true));
        await issuer.RevokeAll(userId);
        await AssertNothingLive(userId);
    }

    private async Task AssertNothingLive(int userId)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        Assert.Equal("", await connection.QuerySingleAsync<string>("SELECT auth_ticket FROM users WHERE id = @userId", new { userId }));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM user_access_tokens WHERE user_id = @userId AND revoked_at IS NULL AND expires_at > @now", new { userId, now }));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM user_remember_tokens WHERE user_id = @userId AND revoked_at IS NULL AND used_at IS NULL", new { userId }));
    }

    private int User(string password = "")
    {
        var id = AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("gen"), password);
        _users.Add(id);
        return id;
    }

    private static async Task<string> Name(int id)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        return await connection.QuerySingleAsync<string>("SELECT username FROM users WHERE id = @id", new { id });
    }

    public void Dispose()
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        connection.Execute("DELETE FROM user_remember_tokens WHERE user_id IN @ids; DELETE FROM user_sessions WHERE user_id IN @ids", new { ids = _users.ToArray() });
        AuthTestDatabase.DeleteUsers(_users);
    }

    private sealed class PausedAccounts(IAccountStore inner) : IAccountStore
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string?> UsernameById(int userId)
        {
            Entered.TrySetResult();
            await Release.Task;
            return await inner.UsernameById(userId);
        }

        public Task<AccountCredentials?> FindByUsername(string username) => inner.FindByUsername(username);
        public Task UpgradePassword(int userId, string current, string replacement) => inner.UpgradePassword(userId, current, replacement);
        public Task<bool> UsernameExists(string username) => inner.UsernameExists(username);
        public Task<bool> EmailExists(string email) => inner.EmailExists(email);
        public Task<int?> Create(NewAccount account) => inner.Create(account);
        public Task RecordAddress(int userId, string address, CredentialScope? scope = null) => inner.RecordAddress(userId, address, scope);
    }

    /// <summary>Pauses after the ticket was exchanged (the authority read), before the access token is written.</summary>
    private sealed class PausedTickets(ISsoTicketStore inner) : ISsoTicketStore
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CredentialOwner?> Exchange(string ticket)
        {
            var owner = await inner.Exchange(ticket);
            Entered.TrySetResult();
            await Release.Task;
            return owner;
        }

        public async Task<CredentialOwner?> ExchangeAt(string ticket, CredentialInstant instant)
        {
            var owner = await inner.ExchangeAt(ticket, instant);
            Entered.TrySetResult();
            await Release.Task;
            return owner;
        }

        public Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null) => inner.Issue(userId, sessionId, scope);
        public Task<IssuedToken> IssueAt(int userId, string? sessionId, CredentialInstant instant, CredentialScope? scope = null) => inner.IssueAt(userId, sessionId, instant, scope);
        public Task<int?> FindUser(string ticket) => inner.FindUser(ticket);
        public Task<int?> FindUserAt(string ticket, CredentialInstant instant) => inner.FindUserAt(ticket, instant);
        public Task<CredentialOwner?> FindOwner(string ticket) => inner.FindOwner(ticket);
        public Task<int?> Consume(string ticket) => inner.Consume(ticket);
        public Task Revoke(int userId, CredentialScope? scope = null) => inner.Revoke(userId, scope);
        public Task RevokeSession(int userId, string sessionId, CredentialScope scope) => inner.RevokeSession(userId, sessionId, scope);
        public Task<CredentialOwner?> Withdraw(int userId, string ticket, CredentialScope scope) => inner.Withdraw(userId, ticket, scope);
    }

    private sealed class PausedBans : IBanLookup
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LoginBan?> Find(string username, string address)
        {
            Entered.TrySetResult();
            await Release.Task;
            return null;
        }

        public Task<LoginBan?> FindAt(string username, string address, DateTimeOffset now) => Find(username, address);
    }
}
