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

        var rotated = await _store.Rotate(first.Value);
        var again = await _store.Rotate(rotated.Token.Value);

        Assert.Equal(new RememberRotation(RememberRotationStatus.Rotated, userId, rotated.Token), rotated);
        Assert.NotEqual(first.Value, rotated.Token.Value);
        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 30 * 86400, rotated.Token.ExpiresAt);
        Assert.Equal(RememberRotationStatus.Rotated, again.Status);
    }

    [AuthDatabaseFact]
    public async Task PresentingARotatedTokenRevokesTheWholeFamilyButNotOtherDevices()
    {
        var userId = User();
        var stolen = await _store.Issue(userId);
        var otherDevice = await _store.Issue(userId);
        var current = (await _store.Rotate(stolen.Value)).Token;

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
        Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(winner.Token.Value)).Status);
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
        var phoneNow = (await _store.Rotate(phone.Value)).Token;

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
    public async Task RevokeAllRacingARotationLeavesNoLiveToken()
    {
        for (var round = 0; round < 10; round++)
        {
            var userId = User();
            var token = await _store.Issue(userId);

            var rotate = Task.Run(() => _store.Rotate(token.Value));
            var revoke = Task.Run(() => _store.RevokeAll(userId));
            await Task.WhenAll(rotate, revoke);

            var rotation = await rotate;
            if (rotation.Status == RememberRotationStatus.Rotated)
                Assert.Equal(RememberRotationStatus.Invalid, (await _store.Rotate(rotation.Token.Value)).Status);
        }
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
