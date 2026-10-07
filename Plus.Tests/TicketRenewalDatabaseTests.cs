using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

/// <summary>An access token can be traded once for a new game ticket and a successor that never
/// outlives it, and only while its session and the user's credentials are live.</summary>
[Collection(AuthDatabaseFactAttribute.Collection)]
public sealed class TicketRenewalDatabaseTests : IDisposable
{
    private const string Address = "203.0.113.8";

    private readonly List<int> _users = [];
    private readonly AuthTestDatabase _database = new();
    private readonly ManualTime _time = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private readonly SsoTicketStore _tickets;
    private readonly AccessTokenStore _access;
    private readonly CredentialGenerations _generations;
    private readonly FakeBans _bans = new();
    private readonly SessionIssuer _issuer;

    public TicketRenewalDatabaseTests()
    {
        var options = AuthTestConfig.Options(c => c.AccessTokenLifetimeMinutes = 60);
        _tickets = new(_database, _time, options);
        _access = new(_database, _time, options);
        _generations = new(_database, _time);
        _issuer = new(_tickets, _access, new RememberTokenStore(_database, _time, options), _generations, new AccountStore(_database, _time, options), _bans, _time);
    }

    [AuthDatabaseFact]
    public async Task RenewalSpendsTheTokenAndItsSuccessorExpiresWithIt()
    {
        var userId = User();
        var login = await Login(userId);
        Assert.Equal(userId, await _tickets.Consume(login.SsoTicket.Value));
        _time.Advance(TimeSpan.FromMinutes(50));

        var renewed = await _issuer.RenewTicket(login.AccessToken.Value, Address);

        Assert.Equal(ResumeStatus.Resumed, renewed.Status);
        var session = renewed.Session!;
        Assert.Equal(userId, session.UserId);
        Assert.Null(await _access.FindOwner(login.AccessToken.Value));
        Assert.Equal(await StoredExpiry(login.AccessToken.Value), await StoredExpiry(session.AccessToken.Value));
        Assert.Equal(await _access.FindOwner(session.AccessToken.Value), await _tickets.FindOwner(session.SsoTicket.Value));
        Assert.Equal(userId, await _tickets.Consume(session.SsoTicket.Value));
        Assert.Equal(ResumeStatus.Invalid, (await _issuer.RenewTicket(login.AccessToken.Value, Address)).Status);

        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(ResumeStatus.Invalid, (await _issuer.RenewTicket(session.AccessToken.Value, Address)).Status);
    }

    [AuthDatabaseFact]
    public async Task ConcurrentRenewalsWithOneTokenIssueOneTicket()
    {
        var userId = User();
        var login = await Login(userId);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _issuer.RenewTicket(login.AccessToken.Value, Address))));

        Assert.Single(results, result => result.Status == ResumeStatus.Resumed);
        Assert.Equal(1, await LiveTokens(userId));
    }

    [AuthDatabaseFact]
    public async Task LogoutAndRevokeAllEndRenewal()
    {
        var userId = User();
        var loggedOut = await Login(userId);
        var otherDevice = await Login(userId);

        await _issuer.Logout(loggedOut.AccessToken.Value, null, null);

        Assert.Equal(ResumeStatus.Invalid, (await _issuer.RenewTicket(loggedOut.AccessToken.Value, Address)).Status);

        var renewed = (await _issuer.RenewTicket(otherDevice.AccessToken.Value, Address)).Session!;
        await _issuer.RevokeAll(userId);

        Assert.Equal(ResumeStatus.Invalid, (await _issuer.RenewTicket(renewed.AccessToken.Value, Address)).Status);
        await AssertNothingLive(userId);
    }

    [AuthDatabaseFact]
    public async Task ABannedUserCannotRenewAndIsSignedOut()
    {
        var userId = User();
        var login = await Login(userId);
        _bans.ByUsernameOrAddress[Address] = new LoginBan("Scamming", null);

        var result = await _issuer.RenewTicket(login.AccessToken.Value, Address);

        Assert.Equal(ResumeStatus.Banned, result.Status);
        await AssertNothingLive(userId);
    }

    private async Task<AuthSession> Login(int userId) => (await _issuer.Issue(userId, "x", await _generations.Current(userId), Address))!;

    private static async Task<DateTime> StoredExpiry(string token)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);

        return await connection.QuerySingleAsync<DateTime>("SELECT expires_at FROM user_access_tokens WHERE token_hash = @hash", new { hash = SecureToken.Hash(token) });
    }

    private async Task<int> LiveTokens(int userId)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);

        return await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM user_access_tokens WHERE user_id = @userId AND revoked_at IS NULL AND expires_at > @now",
            new { userId, now = _time.Now.UtcDateTime });
    }

    private async Task AssertNothingLive(int userId)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        Assert.Equal("", await connection.QuerySingleAsync<string>("SELECT auth_ticket FROM users WHERE id = @userId", new { userId }));
        Assert.Equal(0, await LiveTokens(userId));
    }

    private int User()
    {
        var id = AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("renew"));
        _users.Add(id);

        return id;
    }

    public void Dispose()
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        connection.Execute("DELETE FROM user_sessions WHERE user_id IN @ids", new { ids = _users.ToArray() });
        AuthTestDatabase.DeleteUsers(_users);
    }
}
