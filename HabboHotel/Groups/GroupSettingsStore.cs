using System.Collections.Immutable;
using System.Globalization;
using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

[Singleton]
public interface IGroupSettingsStore
{
    bool Update(int groupId, GroupType type, bool adminOnlyDeco, bool forumEnabled, ImmutableArray<int> requestsToRemove);
}

public sealed class GroupSettingsStore(IDatabase database) : IGroupSettingsStore
{
    public bool Update(int groupId, GroupType type, bool adminOnlyDeco, bool forumEnabled, ImmutableArray<int> requestsToRemove)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var updated = connection.Execute(
            "UPDATE groups SET `state` = @state, admindeco = @adminOnlyDeco, forum_enabled = @forumEnabled WHERE id = @groupId LIMIT 1",
            new
            {
                groupId,
                state = ((int)type).ToString(CultureInfo.InvariantCulture),
                adminOnlyDeco,
                forumEnabled
            }, transaction);

        if (updated != 1 && !connection.ExecuteScalar<bool>(
                "SELECT EXISTS(SELECT 1 FROM groups WHERE id = @groupId)", new
                {
                    groupId
                }, transaction))
        {
            return false;
        }

        if (!requestsToRemove.IsEmpty)
        {
            connection.Execute(
                "DELETE FROM group_requests WHERE group_id = @groupId AND user_id IN @requestsToRemove",
                new
                {
                    groupId,
                    requestsToRemove = requestsToRemove.ToArray()
                }, transaction);
        }

        transaction.Commit();

        return true;
    }
}
