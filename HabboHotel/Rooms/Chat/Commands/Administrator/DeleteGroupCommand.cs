using Plus.HabboHotel.Permissions;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Administrator;

internal class DeleteGroupCommand : IChatCommand
{
    private readonly IGroupManager _groupManager;
    private readonly IRoomManager _roomManager;
    private readonly IDatabase _database;
    private readonly IAccessControl _access;
    public string Key => "deletegroup";

    public string Parameters => "";

    public string Description => "Delete a group from the database and cache.";

    public DeleteGroupCommand(IGroupManager groupManager, IRoomManager roomManager, IDatabase database, IAccessControl access)
    {
        _groupManager = groupManager;
        _roomManager = roomManager;
        _database = database;
        _access = access;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return;
        if (room.Group == null)
        {
            session.SendWhisper("Oops, there is no group here?");
            return;
        }
        if (room.Group.CreatorId != session.GetHabbo().Id && !_access.Outranks(session.GetHabbo().Id, room.Group.CreatorId))
            return;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery($"DELETE FROM `groups` WHERE `id` = '{room.Group.Id}'");
            dbClient.RunQuery($"DELETE FROM `group_memberships` WHERE `group_id` = '{room.Group.Id}'");
            dbClient.RunQuery($"DELETE FROM `group_requests` WHERE `group_id` = '{room.Group.Id}'");
            dbClient.RunQuery($"UPDATE `rooms` SET `group_id` = '0' WHERE `group_id` = '{room.Group.Id}' LIMIT 1");
            dbClient.RunQuery($"UPDATE `user_statistics` SET `groupid` = '0' WHERE `groupid` = '{room.Group.Id}' LIMIT 1");
            dbClient.RunQuery($"DELETE FROM `items_groups` WHERE `group_id` = '{room.Group.Id}'");
        }
        _groupManager.DeleteGroup(room.Group.Id);
        room.Group = null;
        _roomManager.UnloadRoom(room.Id);
        session.SendNotification("Success, group deleted.");
    }
}