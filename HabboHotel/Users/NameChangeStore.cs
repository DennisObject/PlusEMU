using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users;

[Singleton]
public interface INameChangeStore
{
    bool Change(int userId, string oldName, string newName, DateTimeOffset changedAt, bool writeLog);
}

public sealed class NameChangeStore(IDatabase database) : INameChangeStore
{
    public bool Change(int userId, string oldName, string newName, DateTimeOffset changedAt, bool writeLog)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var updated = connection.Execute(
            "UPDATE users SET username = @newName, last_change = @changedAt " +
            "WHERE id = @userId AND username = @oldName",
            new
            {
                userId,
                oldName,
                newName,
                changedAt = changedAt.UtcDateTime
            },
            transaction);

        if (updated != 1)
        {
            return false;
        }

        if (writeLog)
        {
            connection.Execute(
                "INSERT INTO logs_client_namechange (user_id, new_name, old_name, `timestamp`) " +
                "VALUES (@userId, @newName, @oldName, @changedAt)",
                new
                {
                    userId,
                    oldName,
                    newName,
                    changedAt = changedAt.UtcDateTime
                },
                transaction);
        }

        transaction.Commit();

        return true;
    }
}
