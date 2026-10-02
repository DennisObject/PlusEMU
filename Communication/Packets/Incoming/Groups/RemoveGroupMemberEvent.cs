using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Dapper;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class RemoveGroupMemberEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IRoomManager _roomManager;
    private readonly IDatabase _database;

    public RemoveGroupMemberEvent(IGroupManager groupManager, IRoomManager roomManager, IDatabase database)
    {
        _groupManager = groupManager;
        _roomManager = roomManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var userId = packet.ReadInt();
        if (packet.HasDataRemaining())
            packet.ReadBool();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (userId == group.CreatorId)
            return Task.CompletedTask;
        if (userId == session.GetHabbo().Id)
        {
            var wasAdmin = group.IsAdmin(userId);
            if (wasAdmin)
                group.TakeAdmin(userId);
            if (group.IsMember(userId))
                group.DeleteMember(userId);
            if (wasAdmin && _roomManager.TryGetRoom(group.RoomId, out var adminRoom))
            {
                var user = adminRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
                if (user != null)
                {
                    user.RemoveStatus("flatctrl 1");
                    user.UpdateNeeded = true;
                    if (user.GetClient() != null)
                        user.GetClient().Send(new YouAreControllerComposer(0));
                }
            }
            using (var connection = _database.Connection())
            {
                connection.Execute(
                    "DELETE FROM `group_memberships` WHERE `group_id` = @groupId AND `user_id` = @userId", new { groupId, userId });
            }
            session.Send(new GroupInfoComposer(group, session));
            if (session.GetHabbo().HabboStats.FavouriteGroupId == groupId)
            {
                session.GetHabbo().HabboStats.FavouriteGroupId = 0;
                using (var connection = _database.Connection())
                {
                    connection.Execute("UPDATE `user_statistics` SET `groupid` = '0' WHERE `id` = @userId LIMIT 1", new { userId });
                }
                if (group.AdminOnlyDeco == 0 && _roomManager.TryGetRoom(group.RoomId, out var room))
                {
                    var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
                    if (user != null)
                    {
                        user.RemoveStatus("flatctrl 1");
                        user.UpdateNeeded = true;
                        if (user.GetClient() != null)
                            user.GetClient().Send(new YouAreControllerComposer(0));
                    }
                }
                if (session.GetHabbo().InRoom && session.GetHabbo().CurrentRoom != null)
                {
                    var user = session.GetHabbo().CurrentRoom.GetRoomUserManager()
                        .GetRoomUserByHabbo(session.GetHabbo().Id);
                    if (user != null)
                    {
                        session.GetHabbo().CurrentRoom
                            .SendPacket(new UpdateFavouriteGroupComposer(group, user.VirtualId));
                    }
                    session.GetHabbo().CurrentRoom
                        .SendPacket(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
                }
                else
                    session.Send(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
            }
            return Task.CompletedTask;
        }
        if (group.CreatorId == session.GetHabbo().Id || group.IsAdmin(session.GetHabbo().Id))
        {
            if (!group.IsMember(userId))
                return Task.CompletedTask;
            if (group.IsAdmin(userId) && group.CreatorId != session.GetHabbo().Id)
            {
                session.SendNotification(
                    "Sorry, only group creators can remove other administrators from the group.");
                return Task.CompletedTask;
            }
            if (group.IsAdmin(userId))
                group.TakeAdmin(userId);
            if (group.IsMember(userId))
                group.DeleteMember(userId);
            using (var connection = _database.Connection())
            {
                connection.Execute("UPDATE `user_statistics` SET `groupid` = 0 WHERE `id` = @userId AND `groupid` = @groupId", new { userId, groupId });
            }
            if (_roomManager.TryGetRoom(group.RoomId, out var room))
            {
                var user = room.GetRoomUserManager().GetRoomUserByHabbo(userId);
                if (user != null)
                {
                    user.RemoveStatus("flatctrl 1");
                    user.RemoveStatus("flatctrl 3");
                    user.UpdateNeeded = true;
                    user.GetClient()?.Send(new YouAreControllerComposer(0));
                }
            }
            var removedClient = PlusEnvironment.Game.ClientManager.GetClientByUserId(userId);
            if (removedClient != null)
            {
                var habbo = removedClient.GetHabbo();
                var stats = habbo.HabboStats;
                if (stats != null && stats.FavouriteGroupId == groupId)
                {
                    stats.FavouriteGroupId = 0;
                    if (habbo.CurrentRoom != null)
                    {
                        var favouriteUser = habbo.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(userId);
                        if (favouriteUser != null)
                            habbo.CurrentRoom.SendPacket(new UpdateFavouriteGroupComposer(group, favouriteUser.VirtualId));
                        habbo.CurrentRoom.SendPacket(new RefreshFavouriteGroupComposer(userId));
                    }
                    else
                        removedClient.Send(new RefreshFavouriteGroupComposer(userId));
                }
                removedClient.Send(new GroupInfoComposer(group, removedClient));
            }
            session.Send(new UnknownGroupComposer(group.Id, userId));
        }
        return Task.CompletedTask;
    }
}
