using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class FollowFriendEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;

    public FollowFriendEvent(IGameClientManager clientManager)
    {
        _clientManager = clientManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var buddyId = packet.ReadInt();
        if (buddyId == 0 || buddyId == session.GetHabbo().Id)
            return Task.CompletedTask;
        var client = _clientManager.GetClientByUserId(buddyId);
        if (client == null || client.GetHabbo() == null)
            return Task.CompletedTask;
        if (!client.GetHabbo().InRoom)
        {
            session.Send(new FollowFriendFailedComposer(FriendFollowError.Unavailable));
            return Task.CompletedTask;
        }
        if (session.GetHabbo().CurrentRoom?.RoomId == client.GetHabbo().CurrentRoom?.RoomId)
            return Task.CompletedTask;
        session.GetHabbo().PendingFollowRoomId = client.GetHabbo().CurrentRoom.RoomId;
        session.Send(new RoomForwardComposer(client.GetHabbo().CurrentRoom.RoomId));
        return Task.CompletedTask;
    }
}