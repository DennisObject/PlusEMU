using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GiveAdminRightsEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IRoomManager _roomManager;

    public GiveAdminRightsEvent(IGroupManager groupManager, IRoomManager roomManager)
    {
        _groupManager = groupManager;
        _roomManager = roomManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var userId = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (session.GetHabbo().Id != group.CreatorId || userId == group.CreatorId || !group.IsMember(userId))
            return Task.CompletedTask;
        var habbo = PlusEnvironment.GetHabboById(userId);
        group.MakeAdmin(userId);
        if (_roomManager.TryGetRoom(group.RoomId, out var room))
        {
            var user = room.GetRoomUserManager().GetRoomUserByHabbo(userId);
            if (user != null)
            {
                if (!user.Statusses.ContainsKey("flatctrl 3"))
                    user.SetStatus("flatctrl 3");
                user.UpdateNeeded = true;
                if (user.GetClient() != null)
                    user.GetClient().Send(new YouAreControllerComposer(3));
            }
        }
        if (habbo != null)
            session.Send(new GroupMemberUpdatedComposer(groupId, habbo, 1));
        else
            session.Send(new UnknownGroupComposer(group.Id, userId));
        return Task.CompletedTask;
    }
}