using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class RememberTokenDatabaseTests : IDisposable
{
    private readonly List<int> _users = [];
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly RememberTokenStore _store;

    public RememberTokenDatabaseTests() =>
        _store = new RememberTokenStore(new AuthTestDatabase(), _time, AuthTestConfig.Options(c => c.RememberTokenLifetimeDays = 30));

    [AuthDatabaseFact]
    public async Task IssuedTokensAreStoredOnlyAsHashesWithTheConfiguredLifetime()
    {
        var userId = User();

        var token = await _store.Issue(userId);

        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 30 * 86400, token.ExpiresAt);
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        Assert.Equal(SecureToken.Hash(token.Value), connection.QuerySingle<string>("SELECT token_hash FROM user_remember_tokens WHERE user_id = @userId", new { userId }));
    }

    [AuthDatabaseFact]
    public async Task EveryUseRotatesTheTokenAndSlidesTheExpiry()
    {
        var userId = User();
        var first = await _store.Issue(userId);
        _time.Advance(TimeSpan.FromDays(10));

        var rotation = await _store.Rotate(first.Value);
        var successor = await _store.Continue(userId, rotation.FamilyId);
        var again = await _store.Rotate(successor.Value);

        Assert.Equal(RememberRotationStatus.Rotated, rotation.Status);
        Assert.Equal(userId, rotation.UserId);
        Assert.NotEqual(first.Value, successor.Value);
        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 30 * 86400, successor.ExpiresAt);
        Assert.Equal(rotation.FamilyId, again.FamilyId);
    }

    [AuthDatabaseFact]
    public async Task PresentingARotatedTokenRevokesTheWholeFamilyButNotOtherDevices()
    {
        var userId = User();
        var stolen = await _store.Issue(userId);
        var otherDevice = await _store.Issue(userId);
        var current = await Use(stolen.Value);

        var replay = await _store.Rotate(stolen.Value);

        Assert.Equal(RememberRotationStatus.Reused, replay.Status);
        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(current.Value)).Status);
        Assert.Equal(RememberRotationStatus.Rotated, (await _store.Rotate(otherDevice.Value)).Status);
    }

    [AuthDatabaseFact]
    public async Task ConcurrentUseOfOneTokenRotatesOnceAndTreatsTheRestAsReuse()
    {
        var userId = User();
        var token = await _store.Issue(userId);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _store.Rotate(token.Value))));

        var winner = Assert.Single(results, r => r.Status == RememberRotationStatus.Rotated);
        Assert.All(results.Where(r => r != winner), r => Assert.Equal(RememberRotationStatus.Reused, r.Status));
    }

    [AuthDatabaseFact]
    public async Task ExpiredUnknownAndRevokedTokensAreInvalid()
    {
        var userId = User();
        var expiring = await _store.Issue(userId);
        var loggedOut = await _store.Issue(userId);

        await _store.RevokeFamily(loggedOut.Value);
        _time.Advance(TimeSpan.FromDays(31));

        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(expiring.Value)).Status);
        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(loggedOut.Value)).Status);
        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(SecureToken.Generate())).Status);
        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate("")).Status);
    }

    [AuthDatabaseFact]
    public async Task LogoutRevokesOnlyThatDevicesFamily()
    {
        var userId = User();
        var phone = await _store.Issue(userId);
        var laptop = await _store.Issue(userId);
        var phoneNow = await Use(phone.Value);

        await _store.RevokeFamily(phone.Value);

        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(phoneNow.Value)).Status);
        Assert.Equal(RememberRotationStatus.Rotated, (await _store.Rotate(laptop.Value)).Status);
    }

    [AuthDatabaseFact]
    public async Task RevokeAllEndsEveryFamilyOfOneUserOnly()
    {
        var userId = User();
        var other = User();
        var first = await _store.Issue(userId);
        var second = await _store.Issue(userId);
        var unrelated = await _store.Issue(other);

        await _store.RevokeAll(userId);

        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(first.Value)).Status);
        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(second.Value)).Status);
        Assert.Equal(RememberRotationStatus.Rotated, (await _store.Rotate(unrelated.Value)).Status);
    }

    [AuthDatabaseFact]
    public async Task RotationReportsTheGenerationReadUnderTheUserLock()
    {
        var userId = User();
        var token = await _store.Issue(userId);
        using (var connection = new MySqlConnection(AuthTestDatabase.ConnectionString))
            connection.Execute("UPDATE users SET credential_generation = 7 WHERE id = @userId", new { userId });

        Assert.Equal(7, (await _store.Rotate(token.Value)).Generation);
    }

    private async Task<IssuedToken> Use(string token)
    {
        var rotation = await _store.Rotate(token);
        Assert.Equal(RememberRotationStatus.Rotated, rotation.Status);
        return await _store.Continue(rotation.UserId, rotation.FamilyId);
    }

    private int User()
    {
        var id = AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("rem"));
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
