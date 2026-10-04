using System.Data;
using System.Security.Cryptography;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>A connection and transaction that credential writes join, holding the users row lock.</summary>
public sealed record CredentialScope(IDbConnection Connection, IDbTransaction Transaction);

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
    /// Runs <paramref name="writes"/> in one transaction holding the user's row lock. Returns false,
    /// writing nothing, when the generation is no longer <paramref name="generation"/> or
    /// <paramref name="sessionId"/> (if given) is unknown or revoked.
    /// </summary>
    Task<bool> WriteIfCurrent(int userId, long generation, string? sessionId, Func<CredentialScope, Task> writes);

    /// <summary>Bumps the generation, revokes every session and runs <paramref name="revocations"/>
    /// in the same transaction.</summary>
    Task Revoke(int userId, Func<CredentialScope, Task> revocations);

    /// <summary>Bumps the generation and revokes every session inside a caller's transaction that
    /// already holds the user's row lock.</summary>
    Task Bump(int userId, CredentialScope scope);

    /// <summary>Revokes one session and runs <paramref name="revocations"/> in the same transaction.</summary>
    Task RevokeSession(int userId, string sessionId, Func<CredentialScope, Task> revocations);

    /// <summary>Opens a session row inside the credential write that first uses it.</summary>
    Task StartSession(int userId, string sessionId, CredentialScope scope);

    /// <summary>Deletes up to <paramref name="batch"/> sessions created before <paramref name="cutoff"/>
    /// that no access or remember token refers to any more.</summary>
    Task<int> PruneSessions(long cutoff, int batch);
}

public class CredentialGenerations : ICredentialGenerations
{
    private readonly IDatabase _database;

    public CredentialGenerations(IDatabase database) => _database = database;

    public static string NewSessionId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public async Task<long> Current(int userId)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<long>("SELECT `credential_generation` FROM `users` WHERE `id` = @userId", new { userId });
    }

    public async Task<bool> WriteIfCurrent(int userId, long generation, string? sessionId, Func<CredentialScope, Task> writes)
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

    public async Task Revoke(int userId, Func<CredentialScope, Task> revocations)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await Lock(connection, transaction, userId);
        var scope = new CredentialScope(connection, transaction);
        await Bump(userId, scope);
        await revocations(scope);
        transaction.Commit();
    }

    public async Task Bump(int userId, CredentialScope scope)
    {
        await scope.Connection.ExecuteAsync("UPDATE `users` SET `credential_generation` = `credential_generation` + 1 WHERE `id` = @userId", new { userId }, scope.Transaction);
        await scope.Connection.ExecuteAsync("UPDATE `user_sessions` SET `revoked_at` = UNIX_TIMESTAMP() WHERE `user_id` = @userId AND `revoked_at` IS NULL",
            new { userId }, scope.Transaction);
    }

    public async Task RevokeSession(int userId, string sessionId, Func<CredentialScope, Task> revocations)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await Lock(connection, transaction, userId);
        await connection.ExecuteAsync("UPDATE `user_sessions` SET `revoked_at` = UNIX_TIMESTAMP() WHERE `id` = @sessionId AND `user_id` = @userId AND `revoked_at` IS NULL",
            new { sessionId, userId }, transaction);
        await revocations(new(connection, transaction));
        transaction.Commit();
    }

    public Task StartSession(int userId, string sessionId, CredentialScope scope) =>
        StartSession(scope.Connection, scope.Transaction, sessionId, userId);

    public async Task<int> PruneSessions(long cutoff, int batch)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteAsync(
            "DELETE FROM `user_sessions` WHERE `created_at` < @cutoff " +
            "AND NOT EXISTS (SELECT 1 FROM `user_access_tokens` WHERE `session_id` = `user_sessions`.`id`) " +
            "AND NOT EXISTS (SELECT 1 FROM `user_remember_tokens` WHERE `family_id` = `user_sessions`.`id`) LIMIT @batch",
            new { cutoff, batch });
    }

    internal static Task StartSession(IDbConnection connection, IDbTransaction? transaction, string sessionId, int userId) =>
        connection.ExecuteAsync("INSERT INTO `user_sessions` (`id`, `user_id`, `created_at`) VALUES (@sessionId, @userId, UNIX_TIMESTAMP())",
            new { sessionId, userId }, transaction);

    /// <summary>Locks the user's row and returns its generation (-1 when the user is gone).</summary>
    internal static async Task<long> Lock(IDbConnection connection, IDbTransaction transaction, int userId) =>
        await connection.ExecuteScalarAsync<long?>("SELECT `credential_generation` FROM `users` WHERE `id` = @userId FOR UPDATE", new { userId }, transaction) ?? -1;
}
