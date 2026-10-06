using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

[Singleton]
public interface IGroupRemovalStore
{
    bool Delete(int groupId);
    bool RemoveMember(int groupId, int userId, bool requireMembership, bool clearFavourite);
    int CountFurniture(int userId, uint roomId);
}

public sealed class GroupRemovalStore(IDatabase database) : IGroupRemovalStore
{
    public bool Delete(int groupId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!connection.Query<int>("SELECT id FROM `groups` WHERE id = @groupId FOR UPDATE",
                new { groupId }, transaction).Any())
            return false;
        connection.Execute("DELETE FROM `group_memberships` WHERE `group_id` = @groupId", new { groupId }, transaction);
        connection.Execute("DELETE FROM `group_requests` WHERE `group_id` = @groupId", new { groupId }, transaction);
        connection.Execute("UPDATE `rooms` SET `group_id` = 0 WHERE `group_id` = @groupId LIMIT 1", new { groupId }, transaction);
        connection.Execute("UPDATE `user_statistics` SET `groupid` = 0 WHERE `groupid` = @groupId", new { groupId }, transaction);
        connection.Execute("DELETE FROM `items_groups` WHERE `group_id` = @groupId", new { groupId }, transaction);
        if (connection.Execute("DELETE FROM `groups` WHERE `id` = @groupId", new { groupId }, transaction) != 1)
            return false;
        transaction.Commit();
        return true;
    }

    public bool RemoveMember(int groupId, int userId, bool requireMembership, bool clearFavourite)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var removed = connection.Execute(
            "DELETE FROM `group_memberships` WHERE `group_id` = @groupId AND `user_id` = @userId",
            new { groupId, userId }, transaction);
        if (requireMembership && removed == 0)
            return false;
        if (clearFavourite)
            connection.Execute(
                "UPDATE `user_statistics` SET `groupid` = 0 WHERE `id` = @userId AND `groupid` = @groupId",
                new { userId, groupId }, transaction);
        transaction.Commit();
        return true;
    }

    public int CountFurniture(int userId, uint roomId)
    {
        using var connection = database.Connection();
        return connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM `items` WHERE `user_id` = @userId AND `room_id` = @roomId",
            new { userId, roomId });
    }
}
