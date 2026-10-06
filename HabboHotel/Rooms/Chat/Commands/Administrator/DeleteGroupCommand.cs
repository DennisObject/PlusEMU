using Plus.HabboHotel.Permissions;
using Dapper;
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
        {
            return;
        }

        if (room.Group == null)
        {
            session.SendWhisper("Oops, there is no group here?");

            return;
        }

        if (room.Group.CreatorId != session.GetHabbo().Id && !_access.Outranks(session.GetHabbo().Id, room.Group.CreatorId))
        {
            return;
        }

        var groupId = room.Group.Id;
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM `groups` WHERE id=@groupId", new
        {
            groupId
        }, transaction);
        connection.Execute("DELETE FROM group_memberships WHERE group_id=@groupId", new
        {
            groupId
        }, transaction);
        connection.Execute("DELETE FROM group_requests WHERE group_id=@groupId", new
        {
            groupId
        }, transaction);
        connection.Execute("UPDATE rooms SET group_id=0 WHERE group_id=@groupId", new
        {
            groupId
        }, transaction);
        connection.Execute("UPDATE user_statistics SET groupid=0 WHERE groupid=@groupId", new
        {
            groupId
        }, transaction);
        connection.Execute("DELETE FROM items_groups WHERE group_id=@groupId", new
        {
            groupId
        }, transaction);
        transaction.Commit();
        _groupManager.DeleteGroup(room.Group.Id);
        room.Group = null;
        _roomManager.UnloadRoom(room.Id);
        session.SendNotification("Success, group deleted.");
    }
}
