using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

[Collection(AuthDatabaseFactAttribute.Collection)]
public sealed class AuthTokenCleanupDatabaseTests : IDisposable
{
    private readonly List<int> _users = [];
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly RememberTokenStore _remember;
    private readonly AccessTokenStore _access;
    private readonly AuthTokenCleanup _cleanup;

    public AuthTokenCleanupDatabaseTests()
    {
        var database = new AuthTestDatabase();
        _remember = new(database, _time, AuthTestConfig.Options(c => c.RememberTokenLifetimeDays = 30));
        _access = new(database, _time, AuthTestConfig.Options(c => c.AccessTokenLifetimeMinutes = 60));
        _cleanup = new(_remember, _access, _time, NullLogger<AuthTokenCleanup>.Instance) { BatchSize = 2 };
    }

    [AuthDatabaseFact]
    public async Task ASlidingRememberFamilyDoesNotPileUpExpiredRows()
    {
        var userId = User();
        var current = await _remember.Issue(userId);
        for (var day = 0; day < 6; day++)
        {
            _time.Advance(TimeSpan.FromDays(20));
            var rotation = await _remember.Rotate(current.Value);
            current = await _remember.Continue(userId, rotation.FamilyId);
        }

        await _cleanup.PruneExpired();

        Assert.Equal(0, Count("SELECT COUNT(*) FROM user_remember_tokens WHERE user_id = @userId AND expires_at < @cutoff", userId));
        Assert.Equal(RememberRotationStatus.Rotated, (await _remember.Rotate(current.Value)).Status);
    }

    [AuthDatabaseFact]
    public async Task ExpiredAccessTokensAreRemovedAndLiveOnesKept()
    {
        var userId = User();
        for (var i = 0; i < 5; i++)
            await _access.Issue(userId);
        _time.Advance(TimeSpan.FromDays(3));
        var live = await _access.Issue(userId);

        await _cleanup.PruneExpired();

        Assert.Equal(1, Count("SELECT COUNT(*) FROM user_access_tokens WHERE user_id = @userId", userId));
        Assert.Equal(userId, await _access.FindUser(live.Value));
    }

    private int Count(string sql, int userId)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        return connection.QuerySingle<int>(sql, new { userId, cutoff = _time.Now.ToUnixTimeSeconds() - 86400 });
    }

    private int User()
    {
        var id = AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("cln"));
        _users.Add(id);
        return id;
    }

    public void Dispose()
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        connection.Execute("DELETE FROM user_remember_tokens WHERE user_id IN @ids", new { ids = _users.ToArray() });
        AuthTestDatabase.DeleteUsers(_users);
    }
}
