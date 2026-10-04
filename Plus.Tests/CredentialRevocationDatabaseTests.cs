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
        _remember = new(_database, TimeProvider.System, options);
        _generations = new(_database);
        _accounts = new(_database, TimeProvider.System, options);
    }

    private SessionIssuer Issuer(IAccountStore? accounts = null, IBanLookup? bans = null) =>
        new(_tickets, _access, _remember, _generations, accounts ?? _accounts, bans ?? new BanLookup(_database, TimeProvider.System));

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
        var session = await issuer.Issue(userId, "x", await issuer.Generation(userId));
        var paused = new PausedTickets(_tickets);
        var racing = new SessionIssuer(paused, _access, _remember, _generations, _accounts, new BanLookup(_database, TimeProvider.System));

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
        var session = await issuer.Issue(userId, "x", await issuer.Generation(userId));

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
        Assert.Null(await issuer.Issue(userId, "x", before, remember: true));
        Assert.NotNull(await issuer.Issue(userId, "x", before + 1, remember: true));
        await issuer.RevokeAll(userId);
        await AssertNothingLive(userId);
    }

    private async Task AssertNothingLive(int userId)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
        connection.Execute("DELETE FROM user_remember_tokens WHERE user_id IN @ids", new { ids = _users.ToArray() });
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
    }

    /// <summary>Pauses after the ticket was exchanged (the authority read), before the access token is written.</summary>
    private sealed class PausedTickets(ISsoTicketStore inner) : ISsoTicketStore
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int?> Exchange(string ticket)
        {
            var userId = await inner.Exchange(ticket);
            Entered.TrySetResult();
            await Release.Task;
            return userId;
        }

        public Task<IssuedToken> Issue(int userId, CredentialScope? scope = null) => inner.Issue(userId, scope);
        public Task<int?> FindUser(string ticket) => inner.FindUser(ticket);
        public Task<int?> Consume(string ticket) => inner.Consume(ticket);
        public Task Revoke(int userId, CredentialScope? scope = null) => inner.Revoke(userId, scope);
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
    }
}
