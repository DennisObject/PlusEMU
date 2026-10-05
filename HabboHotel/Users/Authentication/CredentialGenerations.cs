using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>A connection and transaction that credential writes join, holding the users row lock.</summary>
/// <param name="CancellationToken">Honoured by every statement run in the scope (set for revocations).</param>
public sealed record CredentialScope(IDbConnection Connection, IDbTransaction Transaction, CancellationToken CancellationToken = default);

/// <summary>Who a presented credential belongs to: the user and the login session it came from.</summary>
public sealed record CredentialOwner(int UserId, string? SessionId);

/// <summary>
/// Versions a user's credentials so revocation is never outlived by a login in flight.
/// users.credential_generation is bumped when everything is revoked (password reset, ban, theft);
/// user_sessions.revoked_at ends one device (logout). Logins capture the generation before their
/// checks and write credentials only while both are unchanged, under the users row lock.
/// </summary>
public interface ICredentialGenerations
{
    Task<long> Current(int userId);

    /// <summary>
    /// Runs <paramref name="writes"/>, which start a new session, in one transaction holding the
    /// user's row lock. Returns false, writing nothing, when the generation is no longer
    /// <paramref name="generation"/>.
    /// </summary>
    Task<bool> WriteIfCurrent(int userId, long generation, Func<CredentialScope, Task> writes);

    /// <summary>
    /// Like <see cref="WriteIfCurrent"/> for credentials of an existing session: also returns false
    /// when <paramref name="sessionId"/> is unknown or revoked (logged out).
    /// </summary>
    Task<bool> WriteInSession(int userId, long generation, string sessionId, Func<CredentialScope, Task> writes);

    /// <summary>Bumps the generation, revokes every session and runs <paramref name="revocations"/>
    /// in the same transaction.</summary>
    Task Revoke(int userId, Func<CredentialScope, Task> revocations, CancellationToken cancellationToken = default);

    /// <summary>Bumps the generation and revokes every session inside a caller's transaction that
    /// already holds the user's row lock.</summary>
    Task Bump(int userId, CredentialScope scope);

    /// <summary>Runs <paramref name="work"/> in one transaction holding the user's row lock, the same
    /// lock every credential write and ticket exchange takes.</summary>
    Task Locked(int userId, Func<CredentialScope, Task> work);

    /// <summary>Marks one session revoked inside a caller's locked transaction.</summary>
    Task MarkSessionRevoked(int userId, string sessionId, CredentialScope scope);

    /// <summary>Opens a session row inside the credential write that first uses it.</summary>
    Task StartSession(int userId, string sessionId, CredentialScope scope);
    Task StartSessionAt(int userId, string sessionId, CredentialInstant instant, CredentialScope scope);

    /// <summary>Deletes up to <paramref name="batch"/> sessions created before <paramref name="cutoff"/>
    /// that no access token, remember token or unexpired ticket refers to any more.</summary>
    Task<int> PruneSessions(DateTimeOffset cutoff, int batch);
}

public class CredentialGenerations : ICredentialGenerations
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;

    public CredentialGenerations(IDatabase database, TimeProvider time)
    {
        _database = database;
        _time = time;
    }

    public static string NewSessionId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public async Task<long> Current(int userId)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<long>("SELECT `credential_generation` FROM `users` WHERE `id` = @userId", new { userId });
    }

    public Task<bool> WriteIfCurrent(int userId, long generation, Func<CredentialScope, Task> writes) => Write(userId, generation, null, writes);

    public Task<bool> WriteInSession(int userId, long generation, string sessionId, Func<CredentialScope, Task> writes) =>
        Write(userId, generation, sessionId ?? throw new ArgumentNullException(nameof(sessionId)), writes);

    private async Task<bool> Write(int userId, long generation, string? sessionId, Func<CredentialScope, Task> writes)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (await Lock(connection, transaction, userId) != generation)
            return false;
        if (sessionId != null && !await connection.ExecuteScalarAsync<bool>(
                "SELECT COUNT(*) FROM `user_sessions` WHERE `id` = @sessionId AND `user_id` = @userId AND `revoked_at` IS NULL",
                new { sessionId, userId }, transaction))
            return false;
        await writes(new(connection, transaction));
        transaction.Commit();
        return true;
    }

    public async Task Revoke(int userId, Func<CredentialScope, Task> revocations, CancellationToken cancellationToken = default)
    {
        // Every step honours the token, so a caller that gives up leaves no revoke that lands later.
        using var connection = (DbConnection)_database.Connection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await Lock(connection, transaction, userId, cancellationToken);
        var scope = new CredentialScope(connection, transaction, cancellationToken);
        await Bump(userId, scope);
        await revocations(scope);
        transaction.Commit();
    }

    public async Task Bump(int userId, CredentialScope scope)
    {
        var now = _time.GetUtcNow();
        await scope.Connection.ExecuteAsync(new CommandDefinition("UPDATE `users` SET `credential_generation` = `credential_generation` + 1 WHERE `id` = @userId",
            new { userId }, scope.Transaction, cancellationToken: scope.CancellationToken));
        await scope.Connection.ExecuteAsync(new CommandDefinition("UPDATE `user_sessions` SET `revoked_at` = @now WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new { userId, now = now.UtcDateTime }, scope.Transaction, cancellationToken: scope.CancellationToken));
    }

    public async Task Locked(int userId, Func<CredentialScope, Task> work)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await Lock(connection, transaction, userId);
        await work(new(connection, transaction));
        transaction.Commit();
    }

    public Task MarkSessionRevoked(int userId, string sessionId, CredentialScope scope)
    {
        var now = _time.GetUtcNow();
        return scope.Connection.ExecuteAsync("UPDATE `user_sessions` SET `revoked_at` = @now WHERE `id` = @sessionId AND `user_id` = @userId AND `revoked_at` IS NULL",
            new { sessionId, userId, now = now.UtcDateTime }, scope.Transaction);
    }

    public Task StartSession(int userId, string sessionId, CredentialScope scope)
        => StartSessionAt(userId, sessionId, CredentialInstant.Capture(_time), scope);

    public Task StartSessionAt(int userId, string sessionId, CredentialInstant instant, CredentialScope scope) =>
        StartSession(scope.Connection, scope.Transaction, sessionId, userId, instant.UtcNow);

    public async Task<int> PruneSessions(DateTimeOffset cutoff, int batch)
    {
        var now = _time.GetUtcNow();
        using var connection = _database.Connection();
        return await connection.ExecuteAsync(
            "DELETE FROM `user_sessions` WHERE `created_at` < @cutoff " +
            "AND NOT EXISTS (SELECT 1 FROM `user_access_tokens` WHERE `session_id` = `user_sessions`.`id`) " +
            "AND NOT EXISTS (SELECT 1 FROM `user_remember_tokens` WHERE `family_id` = `user_sessions`.`id`) " +
            "AND NOT EXISTS (SELECT 1 FROM `users` WHERE `users`.`id` = `user_sessions`.`user_id` AND `auth_ticket_session` = `user_sessions`.`id` " +
            "AND `auth_ticket_expires_at` >= @now) LIMIT @batch",
            new { cutoff = cutoff.UtcDateTime, now = now.UtcDateTime, batch });
    }

    internal static Task StartSession(IDbConnection connection, IDbTransaction? transaction, string sessionId, int userId, DateTimeOffset now) =>
        connection.ExecuteAsync("INSERT INTO `user_sessions` (`id`, `user_id`, `created_at`) VALUES (@sessionId, @userId, @now)",
            new { sessionId, userId, now = now.UtcDateTime }, transaction);

    /// <summary>Locks the user's row and returns its generation (-1 when the user is gone).</summary>
    internal static async Task<long> Lock(IDbConnection connection, IDbTransaction transaction, int userId, CancellationToken cancellationToken = default) =>
        await connection.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT `credential_generation` FROM `users` WHERE `id` = @userId FOR UPDATE",
            new { userId }, transaction, cancellationToken: cancellationToken)) ?? -1;
}
