using System.Data;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class RememberTokenStore : IRememberTokenStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly int _lifetimeSeconds;

    public RememberTokenStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetimeSeconds = options.Value.RememberTokenLifetimeDays * 24 * 60 * 60;
    }

    public Task<IssuedToken> Issue(int userId, CredentialScope? scope = null) =>
        Continue(userId, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), scope);

    public async Task<RememberRotation> Rotate(string token)
    {
        if (string.IsNullOrEmpty(token))
            return new(RememberRotationStatus.Invalid);

        var hash = SecureToken.Hash(token);
        using var connection = _database.Connection();
        var userId = await connection.ExecuteScalarAsync<int?>("SELECT `user_id` FROM `user_remember_tokens` WHERE `token_hash` = @hash", new { hash });
        if (userId == null)
            return new(RememberRotationStatus.Invalid);

        connection.Open();
        using var transaction = connection.BeginTransaction();
        // Locking the user row orders this against revoke-all; the generation read under the lock is
        // what the caller's credential writes must still match.
        var generation = await CredentialGenerations.Lock(connection, transaction, userId.Value);
        var row = await connection.QuerySingleAsync<RememberRow>(
            "SELECT `family_id` AS FamilyId, `expires_at` AS ExpiresAt, `used_at` AS UsedAt, `revoked_at` AS RevokedAt " +
            "FROM `user_remember_tokens` WHERE `token_hash` = @hash FOR UPDATE", new { hash }, transaction);

        if (row.UsedAt != null)
        {
            // Someone presented a token that was already traded in: assume it was stolen.
            await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `family_id` = @FamilyId AND `revoked_at` IS NULL",
                new { now = Now(), row.FamilyId }, transaction);
            transaction.Commit();
            return new(RememberRotationStatus.Reused, userId.Value, row.FamilyId);
        }
        if (row.RevokedAt != null || row.ExpiresAt <= Now() || generation < 0)
            return new(RememberRotationStatus.Invalid);

        await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `used_at` = @now WHERE `token_hash` = @hash", new { now = Now(), hash }, transaction);
        transaction.Commit();
        return new(RememberRotationStatus.Rotated, userId.Value, row.FamilyId, generation);
    }

    public async Task<IssuedToken> Continue(int userId, string familyId, CredentialScope? scope = null)
    {
        var now = Now();
        var token = new IssuedToken(SecureToken.Generate(), now + _lifetimeSeconds);
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(
            "INSERT INTO `user_remember_tokens` (`user_id`, `family_id`, `token_hash`, `created_at`, `expires_at`) VALUES (@userId, @familyId, @hash, @now, @expiresAt)",
            new { userId, familyId, hash = SecureToken.Hash(token.Value), now, expiresAt = token.ExpiresAt }, scope?.Transaction);
        return token;
    }

    public async Task RevokeFamily(string token)
    {
        if (string.IsNullOrEmpty(token))
            return;
        using var connection = _database.Connection();
        await connection.ExecuteAsync(
            "UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `revoked_at` IS NULL AND `family_id` = " +
            "(SELECT `family_id` FROM (SELECT `family_id` FROM `user_remember_tokens` WHERE `token_hash` = @hash) AS `family`)",
            new { now = Now(), hash = SecureToken.Hash(token) });
    }

    public async Task RevokeAll(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync("UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new { now = Now(), userId }, scope?.Transaction);
    }

    public async Task<int> Prune(long cutoff, int batch)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteAsync("DELETE FROM `user_remember_tokens` WHERE `expires_at` < @cutoff LIMIT @batch", new { cutoff, batch });
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();

    private sealed class RememberRow
    {
        public string FamilyId { get; set; } = "";
        public long ExpiresAt { get; set; }
        public long? UsedAt { get; set; }
        public long? RevokedAt { get; set; }
    }
}
