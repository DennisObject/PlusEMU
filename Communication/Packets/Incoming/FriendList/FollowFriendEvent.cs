using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class FollowFriendEvent(IMessengerNavigationService navigation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        navigation.Follow(session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
