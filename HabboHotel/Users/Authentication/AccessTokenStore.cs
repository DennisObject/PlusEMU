using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class AccessTokenStore : IAccessTokenStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly int _lifetimeSeconds;

    public AccessTokenStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetimeSeconds = options.Value.AccessTokenLifetimeMinutes * 60;
    }

    public async Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null)
    {
        var now = Now();
        var token = new IssuedToken(SecureToken.Generate(), now + _lifetimeSeconds);
        using var owned = scope == null ? _database.Connection() : null;
        var connection = scope?.Connection ?? owned!;
        await connection.ExecuteAsync(
            "INSERT INTO `user_access_tokens` (`user_id`, `session_id`, `token_hash`, `created_at`, `expires_at`) VALUES (@userId, @sessionId, @hash, @now, @expiresAt)",
            new { userId, sessionId, hash = SecureToken.Hash(token.Value), now, expiresAt = token.ExpiresAt }, scope?.Transaction);
        return token;
    }

    public async Task<int?> FindUser(string token) => (await FindOwner(token))?.UserId;

    public async Task<CredentialOwner?> FindOwner(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;
        using var connection = _database.Connection();
        return await connection.QueryFirstOrDefaultAsync<CredentialOwner>(
            "SELECT `user_id` AS UserId, `session_id` AS SessionId FROM `user_access_tokens` WHERE `token_hash` = @hash AND `revoked_at` IS NULL AND `expires_at` > @now LIMIT 1",
            new { hash = SecureToken.Hash(token), now = Now() });
    }

    public async Task Revoke(string token)
    {
        if (string.IsNullOrEmpty(token))
            return;
        using var connection = _database.Connection();
        await connection.ExecuteAsync("UPDATE `user_access_tokens` SET `revoked_at` = @now WHERE `token_hash` = @hash AND `revoked_at` IS NULL",
            new { hash = SecureToken.Hash(token), now = Now() });
    }

    public async Task RevokeAll(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync("UPDATE `user_access_tokens` SET `revoked_at` = @now WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new { userId, now = Now() }, scope?.Transaction);
    }

    public Task RevokeSession(string sessionId, CredentialScope scope) =>
        scope.Connection.ExecuteAsync("UPDATE `user_access_tokens` SET `revoked_at` = @now WHERE `session_id` = @sessionId AND `revoked_at` IS NULL",
            new { sessionId, now = Now() }, scope.Transaction);

    public async Task<int> Prune(long cutoff, int batch)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteAsync("DELETE FROM `user_access_tokens` WHERE `expires_at` < @cutoff LIMIT @batch", new { cutoff, batch });
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();
}
