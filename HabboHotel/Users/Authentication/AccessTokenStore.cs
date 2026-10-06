using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class AccessTokenStore : IAccessTokenStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;

    public AccessTokenStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetime = TimeSpan.FromMinutes(options.Value.AccessTokenLifetimeMinutes);

        if (_lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Access token lifetime must be positive.");
        }
    }

    public Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null) =>
        IssueAt(userId, sessionId, CredentialInstant.Capture(_time), scope);

    public async Task<IssuedToken> IssueAt(int userId, string? sessionId, CredentialInstant instant, CredentialScope? scope = null)
    {
        var now = instant.UtcNow;
        var token = new IssuedToken(SecureToken.Generate(), now.Add(_lifetime));
        using var owned = scope == null ? _database.Connection() : null;
        var connection = scope?.Connection ?? owned!;
        await connection.ExecuteAsync(
            "INSERT INTO `user_access_tokens` (`user_id`, `session_id`, `token_hash`, `created_at`, `expires_at`) VALUES (@userId, @sessionId, @hash, @now, @expiresAt)",
            new
            {
                userId,
                sessionId,
                hash = SecureToken.Hash(token.Value),
                now = now.UtcDateTime,
                expiresAt = token.ExpiresAt.UtcDateTime
            }, scope?.Transaction);

        return token;
    }

    public async Task<int?> FindUser(string token) => (await FindOwner(token))?.UserId;

    public async Task<CredentialOwner?> FindOwner(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        using var connection = _database.Connection();

        return await connection.QueryFirstOrDefaultAsync<CredentialOwner>(
            "SELECT `user_id` AS UserId, `session_id` AS SessionId FROM `user_access_tokens` WHERE `token_hash` = @hash AND `revoked_at` IS NULL AND `expires_at` > @now LIMIT 1",
            new
            {
                hash = SecureToken.Hash(token),
                now = _time.GetUtcNow().UtcDateTime
            });
    }

    public async Task Revoke(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        using var connection = _database.Connection();
        await connection.ExecuteAsync("UPDATE `user_access_tokens` SET `revoked_at` = @now WHERE `token_hash` = @hash AND `revoked_at` IS NULL",
            new
            {
                hash = SecureToken.Hash(token),
                now = _time.GetUtcNow().UtcDateTime
            });
    }

    public async Task RevokeAll(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(new CommandDefinition("UPDATE `user_access_tokens` SET `revoked_at` = @now WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new
            {
                userId,
                now = _time.GetUtcNow().UtcDateTime
            }, scope?.Transaction, cancellationToken: scope?.CancellationToken ?? default));
    }

    public Task RevokeSession(string sessionId, CredentialScope scope) =>
        scope.Connection.ExecuteAsync("UPDATE `user_access_tokens` SET `revoked_at` = @now WHERE `session_id` = @sessionId AND `revoked_at` IS NULL",
            new
            {
                sessionId,
                now = _time.GetUtcNow().UtcDateTime
            }, scope.Transaction);

    public async Task<int> Prune(DateTimeOffset cutoff, int batch)
    {
        using var connection = _database.Connection();

        return await connection.ExecuteAsync("DELETE FROM `user_access_tokens` WHERE `expires_at` < @cutoff LIMIT @batch", new
        {
            cutoff = cutoff.UtcDateTime,
            batch
        });
    }
}
