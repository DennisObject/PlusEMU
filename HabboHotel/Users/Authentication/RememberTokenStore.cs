using System.Data;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class RememberTokenStore : IRememberTokenStore
{
    // Expired rows are kept a day, then removed when new families are issued.
    private const int RetentionSeconds = 24 * 60 * 60;

    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly int _lifetimeSeconds;

    public RememberTokenStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetimeSeconds = options.Value.RememberTokenLifetimeDays * 24 * 60 * 60;
    }

    public async Task<IssuedToken> Issue(int userId)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("DELETE FROM `user_remember_tokens` WHERE `expires_at` < @cutoff", new { cutoff = Now() - RetentionSeconds });
        return await Insert(connection, null, userId, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
    }

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
        await LockUser(connection, transaction, userId.Value);
        var row = await connection.QuerySingleAsync<RememberRow>(
            "SELECT `family_id` AS FamilyId, `expires_at` AS ExpiresAt, `used_at` AS UsedAt, `revoked_at` AS RevokedAt " +
            "FROM `user_remember_tokens` WHERE `token_hash` = @hash FOR UPDATE", new { hash }, transaction);

        if (row.UsedAt != null)
        {
            // Someone presented a token that was already traded in: assume it was stolen.
            await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `family_id` = @FamilyId AND `revoked_at` IS NULL",
                new { now = Now(), row.FamilyId }, transaction);
            transaction.Commit();
            return new(RememberRotationStatus.Reused, userId.Value);
        }
        if (row.RevokedAt != null || row.ExpiresAt <= Now())
            return new(RememberRotationStatus.Invalid);

        await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `used_at` = @now WHERE `token_hash` = @hash", new { now = Now(), hash }, transaction);
        var successor = await Insert(connection, transaction, userId.Value, row.FamilyId);
        transaction.Commit();
        return new(RememberRotationStatus.Rotated, userId.Value, successor);
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

    public async Task RevokeAll(int userId)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await LockUser(connection, transaction, userId);
        await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new { now = Now(), userId }, transaction);
        transaction.Commit();
    }

    /// <summary>Rotation and revoke-all lock the user's row first, so they run one after the other
    /// per user (a revoke sees any successor a rotation inserted) and cannot deadlock each other.</summary>
    private static Task LockUser(IDbConnection connection, IDbTransaction transaction, int userId) =>
        connection.ExecuteAsync("SELECT `id` FROM `users` WHERE `id` = @userId FOR UPDATE", new { userId }, transaction);

    private async Task<IssuedToken> Insert(IDbConnection connection, IDbTransaction? transaction, int userId, string familyId)
    {
        var now = Now();
        var token = new IssuedToken(SecureToken.Generate(), now + _lifetimeSeconds);
        await connection.ExecuteAsync(
            "INSERT INTO `user_remember_tokens` (`user_id`, `family_id`, `token_hash`, `created_at`, `expires_at`) VALUES (@userId, @familyId, @hash, @now, @expiresAt)",
            new { userId, familyId, hash = SecureToken.Hash(token.Value), now, expiresAt = token.ExpiresAt }, transaction);
        return token;
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
