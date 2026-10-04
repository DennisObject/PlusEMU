using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Dapper;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class UpdateGroupSettingsEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IGroupInfoSnapshotService _groupInfo;
    private readonly IRoomManager _roomManager;
    private readonly IDatabase _database;

    public UpdateGroupSettingsEvent(IGroupManager groupManager, IRoomManager roomManager, IDatabase database, IGroupInfoSnapshotService groupInfo)
    {
        _groupInfo = groupInfo;
        _groupManager = groupManager;
        _roomManager = roomManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (group.CreatorId != session.GetHabbo().Id)
            return Task.CompletedTask;
        var type = packet.ReadInt();
        var furniOptions = packet.ReadInt();
        var forumEnabled = packet.ReadBool();
        switch (type)
        {
            default:
                group.Type = GroupType.Open;
                break;
            case 1:
                group.Type = GroupType.Locked;
                break;
            case 2:
                group.Type = GroupType.Private;
                break;
        }
        if (group.Type != GroupType.Locked)
        {
            if (group.GetRequests.Count > 0)
            {
                foreach (var userId in group.GetRequests.ToList()) group.HandleRequest(userId, false);
                group.ClearRequests();
            }
        }
        using (var connection = _database.Connection())
        {
            connection.Execute("UPDATE `groups` SET `state` = @groupState, `admindeco` = @adminDeco, `forum_enabled` = @forumEnabled WHERE `id` = @groupId LIMIT 1",
                new { groupState = (group.Type == GroupType.Open ? 0 : group.Type == GroupType.Locked ? 1 : 2).ToString(), adminDeco = (furniOptions == 1 ? 1 : 0).ToString(), forumEnabled = forumEnabled ? "1" : "0", groupId = group.Id });
        }
        group.AdminOnlyDeco = furniOptions;
        group.ForumEnabled = forumEnabled;
        group.HasForum = forumEnabled;
        if (_roomManager.TryGetRoom(group.RoomId, out var room))
        {
            foreach (var user in room.GetRoomUserManager().GetRoomUsers().ToList())
            {
                if (room.OwnerId == user.UserId || group.IsAdmin(user.UserId) || !group.IsMember(user.UserId))
                    continue;
                if (furniOptions == 1)
                {
                    user.RemoveStatus("flatctrl 1");
                    user.UpdateNeeded = true;
                    user.GetClient().Send(new YouAreControllerComposer(0));
                }
                else if (furniOptions == 0 && !user.Statusses.ContainsKey("flatctrl 1"))
                {
                    user.SetStatus("flatctrl 1");
                    user.UpdateNeeded = true;
                    user.GetClient().Send(new YouAreControllerComposer(1));
                }
            }
        }
        session.Send(new GroupInfoComposer(_groupInfo.Capture(group, session.GetHabbo().Id)));
        return Task.CompletedTask;
    }
}