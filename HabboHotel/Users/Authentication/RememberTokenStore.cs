using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class RememberTokenStore : IRememberTokenStore
{
    /// <summary>Successors a used token may still mint within the grace window.</summary>
    public const int MaxGraceRetries = 3;

    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _grace;

    public RememberTokenStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetime = TimeSpan.FromDays(options.Value.RememberTokenLifetimeDays);
        _grace = TimeSpan.FromSeconds(options.Value.RememberReuseGraceSeconds);

        if (_lifetime <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), "Remember token lifetime must be positive.");
        }

        if (_grace < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), "Remember reuse grace must not be negative.");
        }
    }

    public async Task<IssuedToken> Issue(int userId, CredentialScope? scope = null)
    {
        var now = _time.GetUtcNow();
        var token = NewToken(now);
        var sessionId = CredentialGenerations.NewSessionId();
        using var owned = scope == null ? _database.Connection() : null;
        await CredentialGenerations.StartSession(scope?.Connection ?? owned!, scope?.Transaction, sessionId, userId, now);

        return await ContinuePrepared(userId, sessionId, now, token, scope);
    }

    public async Task<RememberRotation> Rotate(string token, Func<int, CredentialScope, Task>? onReuse = null)
        => await RotateAt(token, CredentialInstant.Capture(_time), onReuse);

    public async Task<RememberRotation> RotateAt(string token, CredentialInstant instant, Func<int, CredentialScope, Task>? onReuse = null)
    {
        if (string.IsNullOrEmpty(token)) {
            return new(RememberRotationStatus.Invalid);
        }

        var hash = SecureToken.Hash(token);
        var now = instant.UtcNow;
        using var connection = _database.Connection();
        var userId = await connection.ExecuteScalarAsync<int?>("SELECT `user_id` FROM `user_remember_tokens` WHERE `token_hash` = @hash", new { hash });

        if (userId == null) {
            return new(RememberRotationStatus.Invalid);
        }

        connection.Open();
        using var transaction = connection.BeginTransaction();
        // Locking the user row orders this against revoke-all; the generation read under the lock is
        // what the caller's credential writes must still match.
        var generation = await CredentialGenerations.Lock(connection, transaction, userId.Value);
        var row = await connection.QuerySingleAsync<RememberRow>(
            "SELECT `family_id` AS FamilyId, `expires_at` AS ExpiresAt, `used_at` AS UsedAt, `grace_uses` AS GraceUses, `revoked_at` AS RevokedAt " +
            "FROM `user_remember_tokens` WHERE `token_hash` = @hash FOR UPDATE", new { hash }, transaction);

        // Revoked (incl. an earlier reuse) or expired tokens change nothing, so replaying an old
        // token cannot keep signing the account out.
        if (row.RevokedAt != null || row.ExpiresAt is not { } expiresAt || expiresAt <= now || generation < 0) {
            return new(RememberRotationStatus.Invalid);
        }

        if (row.UsedAt != null && now - row.UsedAt < _grace && row.GraceUses < MaxGraceRetries) {
            // Presented again moments after its use: the client most likely lost the response or
            // a second tab raced it. Its family gets another successor; the first one stays valid.
            await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `grace_uses` = `grace_uses` + 1 WHERE `token_hash` = @hash", new { hash }, transaction);
            transaction.Commit();

            return new(RememberRotationStatus.Rotated, userId.Value, row.FamilyId, generation);
        }

        if (row.UsedAt != null) {
            // A live token that was already traded in: someone else holds it.
            var scope = new CredentialScope(connection, transaction);
            await RevokeSessionAt(row.FamilyId, scope, now);

            if (onReuse != null) {
                await onReuse(userId.Value, scope);
            }

            transaction.Commit();

            return new(RememberRotationStatus.Reused, userId.Value, row.FamilyId);
        }

        await connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `used_at` = @now WHERE `token_hash` = @hash", new { now = now.UtcDateTime, hash }, transaction);
        transaction.Commit();

        return new(RememberRotationStatus.Rotated, userId.Value, row.FamilyId, generation);
    }

    public Task<IssuedToken> Continue(int userId, string familyId, CredentialScope? scope = null) =>
        ContinueAt(userId, familyId, CredentialInstant.Capture(_time), scope);

    public Task<IssuedToken> ContinueAt(int userId, string familyId, CredentialInstant instant, CredentialScope? scope = null)
    {
        var now = instant.UtcNow;

        return ContinuePrepared(userId, familyId, now, NewToken(now), scope);
    }

    private async Task<IssuedToken> ContinuePrepared(int userId, string familyId, DateTimeOffset now, IssuedToken token, CredentialScope? scope)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(
            "INSERT INTO `user_remember_tokens` (`user_id`, `family_id`, `token_hash`, `created_at`, `expires_at`) VALUES (@userId, @familyId, @hash, @now, @expiresAt)",
            new { userId, familyId, hash = SecureToken.Hash(token.Value), now = now.UtcDateTime, expiresAt = token.ExpiresAt.UtcDateTime }, scope?.Transaction);

        return token;
    }

    private IssuedToken NewToken(DateTimeOffset now) => new(SecureToken.Generate(), now.Add(_lifetime));

    public async Task<CredentialOwner?> FindOwner(string token)
    {
        if (string.IsNullOrEmpty(token)) {
            return null;
        }

        using var connection = _database.Connection();

        return await connection.QueryFirstOrDefaultAsync<CredentialOwner>(
            "SELECT `user_id` AS UserId, `family_id` AS SessionId FROM `user_remember_tokens` WHERE `token_hash` = @hash", new { hash = SecureToken.Hash(token) });
    }

    public Task RevokeSession(string sessionId, CredentialScope scope) => RevokeSessionAt(sessionId, scope, _time.GetUtcNow());

    private static Task RevokeSessionAt(string sessionId, CredentialScope scope, DateTimeOffset now) =>
        scope.Connection.ExecuteAsync("UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `family_id` = @sessionId AND `revoked_at` IS NULL",
            new { now = now.UtcDateTime, sessionId }, scope.Transaction);

    public async Task RevokeAll(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(new CommandDefinition("UPDATE `user_remember_tokens` SET `revoked_at` = @now WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new { now = _time.GetUtcNow().UtcDateTime, userId }, scope?.Transaction, cancellationToken: scope?.CancellationToken ?? default));
    }

    public async Task<int> Prune(DateTimeOffset cutoff, int batch)
    {
        using var connection = _database.Connection();

        return await connection.ExecuteAsync("DELETE FROM `user_remember_tokens` WHERE `expires_at` < @cutoff LIMIT @batch", new { cutoff = cutoff.UtcDateTime, batch });
    }

    private sealed class RememberRow
    {
        public string FamilyId { get; set; } = "";
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? UsedAt { get; set; }
        public int GraceUses { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
    }
}
