using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users.Ignores;

[Singleton]
public interface IPlayerIgnoreStore
{
    Task<bool> SetIgnored(int userId, int targetId, bool ignored);
}

public sealed class PlayerIgnoreStore(IDatabase database) : IPlayerIgnoreStore
{
    public async Task<bool> SetIgnored(int userId, int targetId, bool ignored)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var account = await connection.QuerySingleOrDefaultAsync<int?>(
            "SELECT id FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction);
        if (account == null)
            return false;

        var affected = ignored
            ? await connection.ExecuteAsync(
                "INSERT INTO user_ignores (user_id, ignore_id) SELECT @userId, @targetId WHERE NOT EXISTS (SELECT 1 FROM user_ignores WHERE user_id = @userId AND ignore_id = @targetId)",
                new { userId, targetId }, transaction)
            : await connection.ExecuteAsync(
                "DELETE FROM user_ignores WHERE user_id = @userId AND ignore_id = @targetId",
                new { userId, targetId }, transaction);
        if (affected is < 0 or > 1)
            throw new InvalidOperationException("Unexpected ignore row count.");

        transaction.Commit();
        return true;
    }
}
