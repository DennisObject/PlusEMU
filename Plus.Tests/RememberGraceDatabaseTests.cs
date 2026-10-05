using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

/// <summary>A remember token presented again shortly after it was used is a lost response or a
/// second tab, not theft: within RememberReuseGraceSeconds it mints another successor.</summary>
[Collection(AuthDatabaseFactAttribute.Collection)]
public sealed class RememberGraceDatabaseTests : IDisposable
{
    private readonly List<int> _users = [];
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly AuthTestDatabase _database = new();
    private readonly CredentialGenerations _generations;
    private readonly AccessTokenStore _access;
    private readonly SessionIssuer _issuer;

    public RememberGraceDatabaseTests()
    {
        var options = AuthTestConfig.Options(c => c.RememberReuseGraceSeconds = 30);
        _generations = new(_database, _time);
        _access = new(_database, TimeProvider.System, options);
        _issuer = new(new SsoTicketStore(_database, TimeProvider.System, options), _access, new RememberTokenStore(_database, _time, options), _generations,
            new AccountStore(_database, TimeProvider.System, options), new BanLookup(_database, TimeProvider.System));
    }

    [AuthDatabaseFact]
    public async Task ARetryWithinGraceGetsASiblingAndBothSuccessorsWorkOnce()
    {
        var (userId, first) = await RememberedLogin();
        var original = await Resume(first);
        _time.Advance(TimeSpan.FromSeconds(10));

        var retry = await Resume(first);

        Assert.Equal(ResumeStatus.Resumed, original.Status);
        Assert.Equal(ResumeStatus.Resumed, retry.Status);
        Assert.Equal(userId, await _access.FindUser(retry.Session!.AccessToken.Value));
        var a = Token(original);
        var b = Token(retry);
        Assert.NotEqual(a, b);
        Assert.Equal(ResumeStatus.Resumed, (await Resume(a)).Status);
        Assert.Equal(ResumeStatus.Resumed, (await Resume(b)).Status);
        Assert.Equal(userId, await _access.FindUser(original.Session!.AccessToken.Value));
    }

    [AuthDatabaseFact]
    public async Task ARetryAfterTheGraceWindowIsTheft()
    {
        var (userId, first) = await RememberedLogin();
        var original = await Resume(first);
        var generation = await _generations.Current(userId);
        _time.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(ResumeStatus.Invalid, (await Resume(first)).Status);

        Assert.Equal(generation + 1, await _generations.Current(userId));
        Assert.Null(await _access.FindUser(original.Session!.AccessToken.Value));
        Assert.Equal(ResumeStatus.Invalid, (await Resume(Token(original))).Status);
    }

    [AuthDatabaseFact]
    public async Task GraceRetriesAreCappedAndTheNextOneIsTheft()
    {
        var (userId, first) = await RememberedLogin();
        await Resume(first);
        var generation = await _generations.Current(userId);

        for (var i = 0; i < RememberTokenStore.MaxGraceRetries; i++)
            Assert.Equal(ResumeStatus.Resumed, (await Resume(first)).Status);
        Assert.Equal(generation, await _generations.Current(userId));

        Assert.Equal(ResumeStatus.Invalid, (await Resume(first)).Status);
        Assert.Equal(generation + 1, await _generations.Current(userId));
    }

    [AuthDatabaseFact]
    public async Task AGraceRetryAfterLogoutIsRefusedWithoutSideEffects()
    {
        var (userId, first) = await RememberedLogin();
        var original = await Resume(first);
        await _issuer.Logout(original.Session!.AccessToken.Value, null, null);
        var generation = await _generations.Current(userId);

        Assert.Equal(ResumeStatus.Invalid, (await Resume(first)).Status);

        Assert.Equal(generation, await _generations.Current(userId));
    }

    [AuthDatabaseFact]
    public async Task ResumedSessionsNameTheirUser()
    {
        var (userId, first) = await RememberedLogin();

        var session = (await Resume(first)).Session!;

        Assert.Equal(userId, session.UserId);
        Assert.Equal(await Name(userId), session.Username);
    }

    private async Task<(int UserId, string Token)> RememberedLogin()
    {
        var userId = AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("grc"));
        _users.Add(userId);
        var session = await _issuer.Issue(userId, await Name(userId), await _generations.Current(userId), "203.0.113.8", remember: true);
        return (userId, session!.RememberToken!.Value.Value);
    }

    private Task<ResumeResult> Resume(string token) => _issuer.Resume(token, "203.0.113.8", withTicket: true);

    private static string Token(ResumeResult result) => result.Session!.RememberToken!.Value.Value;

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
}
