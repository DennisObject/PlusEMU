using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class AuthTokenDatabaseTests : IDisposable
{
    private readonly List<int> _users = [];
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly AuthTestDatabase _database = new();

    [AuthDatabaseFact]
    public void SecureLoginUpdateIsRepeatable()
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        var migration = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Resources/SQLs/Updates/19_SecureLoginTokens.sql")));

        connection.Execute(migration);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users' AND column_name = 'auth_ticket_expires_at'"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'user_access_tokens' AND index_name = 'token_hash' AND non_unique = 0"));
    }

    [AuthDatabaseFact]
    public async Task SsoTicketLogsInOnceAndOnlyBeforeItExpires()
    {
        var store = new SsoTicketStore(_database, _time, AuthTestConfig.Options(c => c.SsoTicketLifetimeSeconds = 60));
        var userId = User();

        var ticket = await store.Issue(userId);
        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 60, ticket.ExpiresAt);
        Assert.Equal(userId, await store.FindUser(ticket.Value));
        Assert.Equal(userId, await store.Consume(ticket.Value));
        Assert.Null(await store.Consume(ticket.Value));
        Assert.Null(await store.FindUser(ticket.Value));

        var expired = await store.Issue(userId);
        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(await store.FindUser(expired.Value));
        Assert.Null(await store.Consume(expired.Value));
    }

    [AuthDatabaseFact]
    public async Task IssuingReplacesThePreviousTicket()
    {
        var store = new SsoTicketStore(_database, _time, AuthTestConfig.Options());
        var userId = User();

        var first = await store.Issue(userId);
        var second = await store.Issue(userId);

        Assert.Null(await store.Consume(first.Value));
        Assert.Equal(userId, await store.Consume(second.Value));
    }

    [AuthDatabaseFact]
    public async Task LegacyTicketsWithoutExpiryAreRejected()
    {
        var store = new SsoTicketStore(_database, _time, AuthTestConfig.Options());
        var userId = User();
        var legacy = SecureToken.Generate();
        using (var connection = new MySqlConnection(AuthTestDatabase.ConnectionString))
            connection.Execute("UPDATE users SET auth_ticket = @legacy, auth_ticket_expires_at = NULL WHERE id = @userId", new { legacy, userId });

        Assert.Null(await store.Consume(legacy));
    }

    [AuthDatabaseFact]
    public async Task ConcurrentGameLoginsWithOneTicketYieldExactlyOneSession()
    {
        var store = new SsoTicketStore(_database, _time, AuthTestConfig.Options());
        var userId = User();
        var ticket = await store.Issue(userId);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.Consume(ticket.Value))));

        Assert.Equal(userId, Assert.Single(results, r => r != null));
    }

    [AuthDatabaseFact]
    public async Task AccessTokensAreStoredHashedAndExpireOrRevoke()
    {
        var store = new AccessTokenStore(_database, _time, AuthTestConfig.Options(c => c.AccessTokenLifetimeMinutes = 10));
        var userId = User();

        var token = await store.Issue(userId);
        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 600, token.ExpiresAt);
        using (var connection = new MySqlConnection(AuthTestDatabase.ConnectionString))
        {
            var stored = connection.QuerySingle<string>("SELECT token_hash FROM user_access_tokens WHERE user_id = @userId", new { userId });
            Assert.Equal(SecureToken.Hash(token.Value), stored);
            Assert.NotEqual(token.Value, stored);
        }
        Assert.Equal(userId, await store.FindUser(token.Value));
        Assert.Null(await store.FindUser(token.Value + "x"));

        await store.Revoke(token.Value);
        Assert.Null(await store.FindUser(token.Value));

        var expiring = await store.Issue(userId);
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(await store.FindUser(expiring.Value));
    }

    [AuthDatabaseFact]
    public async Task RevokeAllSignsOutEveryHttpSessionOfOneUser()
    {
        var store = new AccessTokenStore(_database, _time, AuthTestConfig.Options());
        var userId = User();
        var other = User();
        var first = await store.Issue(userId);
        var second = await store.Issue(userId);
        var unrelated = await store.Issue(other);

        await store.RevokeAll(userId);

        Assert.Null(await store.FindUser(first.Value));
        Assert.Null(await store.FindUser(second.Value));
        Assert.Equal(other, await store.FindUser(unrelated.Value));
    }

    [AuthDatabaseFact]
    public async Task LeaderboardBearerLookupResolvesTheViewerWithPlainSql()
    {
        // The query the Python badge leaderboard uses to resolve its viewer.
        const string viewerSql = "SELECT user_id FROM user_access_tokens WHERE token_hash = SHA2(@token, 256) AND revoked_at IS NULL AND expires_at > UNIX_TIMESTAMP() LIMIT 1";
        var store = new AccessTokenStore(_database, TimeProvider.System, AuthTestConfig.Options());
        var userId = User();
        var token = await store.Issue(userId);
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);

        Assert.Equal(userId, connection.QuerySingleOrDefault<int?>(viewerSql, new { token = token.Value }));
        await store.Revoke(token.Value);
        Assert.Null(connection.QuerySingleOrDefault<int?>(viewerSql, new { token = token.Value }));
    }

    private int User()
    {
        var id = AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("tok"));
        _users.Add(id);
        return id;
    }

    public void Dispose() => AuthTestDatabase.DeleteUsers(_users);
}
