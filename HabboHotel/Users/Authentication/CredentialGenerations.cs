using System.Data;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>A connection and transaction that credential writes join, holding the users row lock.</summary>
public sealed record CredentialScope(IDbConnection Connection, IDbTransaction Transaction);

/// <summary>
/// users.credential_generation: bumped whenever all of a user's credentials are revoked. A login
/// captures it before its checks and only writes credentials if it is unchanged, so a revoke that
/// lands mid-login cannot be outlived by what that login issues.
/// </summary>
public interface ICredentialGenerations
{
    Task<long> Current(int userId);

    /// <summary>Runs <paramref name="writes"/> in one transaction holding the user's row lock.
    /// Returns false, writing nothing, when the generation is no longer <paramref name="generation"/>.</summary>
    Task<bool> WriteIfCurrent(int userId, long generation, Func<CredentialScope, Task> writes);

    /// <summary>Bumps the generation and runs <paramref name="revocations"/> in the same transaction.</summary>
    Task Revoke(int userId, Func<CredentialScope, Task> revocations);
}

public class CredentialGenerations : ICredentialGenerations
{
    private readonly IDatabase _database;

    public CredentialGenerations(IDatabase database) => _database = database;

    public async Task<long> Current(int userId)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<long>("SELECT `credential_generation` FROM `users` WHERE `id` = @userId", new { userId });
    }

    public async Task<bool> WriteIfCurrent(int userId, long generation, Func<CredentialScope, Task> writes)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (await Lock(connection, transaction, userId) != generation)
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
        await connection.ExecuteAsync("UPDATE `users` SET `credential_generation` = `credential_generation` + 1 WHERE `id` = @userId", new { userId }, transaction);
        await revocations(new(connection, transaction));
        transaction.Commit();
    }

    /// <summary>Locks the user's row and returns its generation (-1 when the user is gone).</summary>
    internal static async Task<long> Lock(IDbConnection connection, IDbTransaction transaction, int userId) =>
        await connection.ExecuteScalarAsync<long?>("SELECT `credential_generation` FROM `users` WHERE `id` = @userId FOR UPDATE", new { userId }, transaction) ?? -1;
}
