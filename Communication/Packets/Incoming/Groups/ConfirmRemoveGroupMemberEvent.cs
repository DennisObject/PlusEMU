using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Dapper;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class ConfirmRemoveGroupMemberEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IDatabase _database;

    public ConfirmRemoveGroupMemberEvent(IGroupManager groupManager, IDatabase database)
    {
        _groupManager = groupManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var userId = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (userId == group.CreatorId)
            return Task.CompletedTask;
        var actorId = session.GetHabbo().Id;
        if (actorId != group.CreatorId && !group.IsAdmin(actorId))
            return Task.CompletedTask;
        if (group.IsAdmin(userId) && actorId != group.CreatorId)
            return Task.CompletedTask;
        if (!group.IsMember(userId))
            return Task.CompletedTask;
        int furnitureCount;
        using (var connection = _database.Connection())
        {
            furnitureCount = connection.QuerySingle<int>(
                "SELECT COUNT(*) FROM `items` WHERE `user_id` = @userId AND `room_id` = @roomId",
                new { userId, roomId = (int)group.RoomId });
        }
        session.Send(new GroupConfirmRemoveMemberComposer(userId, furnitureCount));
        return Task.CompletedTask;
    }
}
