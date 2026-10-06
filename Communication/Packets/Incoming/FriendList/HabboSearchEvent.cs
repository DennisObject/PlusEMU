using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class HabboSearchEvent(IMessengerNavigationService navigation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        navigation.Search(session, packet.ReadString());

        return Task.CompletedTask;
    }
}
