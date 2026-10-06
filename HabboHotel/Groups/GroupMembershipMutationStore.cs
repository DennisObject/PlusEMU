using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

[Singleton]
public interface IGroupMembershipMutationStore
{
    bool Accept(int groupId, int userId);
    bool Decline(int groupId, int userId);
    bool SetAdmin(int groupId, int userId, bool isAdmin);
}

public sealed class GroupMembershipMutationStore(IDatabase database) : IGroupMembershipMutationStore
{
    public bool Accept(int groupId, int userId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var inserted = connection.Execute(
            "INSERT INTO group_memberships (user_id, group_id, `rank`) VALUES (@userId, @groupId, 0)",
            new { userId, groupId }, transaction);
        var removed = connection.Execute(
            "DELETE FROM group_requests WHERE user_id = @userId AND group_id = @groupId LIMIT 1",
            new { userId, groupId }, transaction);
        if (inserted != 1 || removed != 1)
            return false;
        transaction.Commit();
        return true;
    }

    public bool SetAdmin(int groupId, int userId, bool isAdmin)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var updated = connection.Execute(
            "UPDATE group_memberships SET `rank` = @rank WHERE user_id = @userId AND group_id = @groupId",
            new { userId, groupId, rank = isAdmin ? 1 : 0 }, transaction);
        if (updated != 1 && !connection.ExecuteScalar<bool>(
                "SELECT EXISTS(SELECT 1 FROM group_memberships WHERE user_id = @userId AND group_id = @groupId)",
                new { userId, groupId }, transaction))
            return false;
        transaction.Commit();
        return true;
    }

    public bool Decline(int groupId, int userId)
    {
        using var connection = database.Connection();
        return connection.Execute(
            "DELETE FROM group_requests WHERE user_id = @userId AND group_id = @groupId LIMIT 1",
            new { userId, groupId }) == 1;
    }
}
