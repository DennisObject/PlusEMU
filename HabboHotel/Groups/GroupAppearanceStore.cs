using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

[Singleton]
public interface IGroupAppearanceStore
{
    bool UpdateIdentity(int groupId, string name, string description);
    bool UpdateBadge(int groupId, string badge);
    bool UpdateColours(int groupId, int mainColour, int secondaryColour);
}

public sealed class GroupAppearanceStore(IDatabase database) : IGroupAppearanceStore
{
    public bool UpdateIdentity(int groupId, string name, string description)
    {
        using var connection = database.Connection();
        var updated = connection.Execute(
            "UPDATE `groups` SET `name` = @name, `desc` = @description WHERE `id` = @groupId LIMIT 1",
            new { groupId, name, description });
        return updated == 1 || Exists(connection, groupId);
    }

    public bool UpdateBadge(int groupId, string badge)
    {
        using var connection = database.Connection();
        var updated = connection.Execute(
            "UPDATE `groups` SET `badge` = @badge WHERE `id` = @groupId LIMIT 1",
            new { groupId, badge });
        return updated == 1 || Exists(connection, groupId);
    }

    public bool UpdateColours(int groupId, int mainColour, int secondaryColour)
    {
        using var connection = database.Connection();
        var updated = connection.Execute(
            "UPDATE `groups` SET `colour1` = @mainColour, `colour2` = @secondaryColour WHERE `id` = @groupId LIMIT 1",
            new { groupId, mainColour, secondaryColour });
        return updated == 1 || Exists(connection, groupId);
    }

    private static bool Exists(System.Data.IDbConnection connection, int groupId) => connection.ExecuteScalar<bool>(
        "SELECT EXISTS(SELECT 1 FROM `groups` WHERE `id` = @groupId)", new { groupId });
}
