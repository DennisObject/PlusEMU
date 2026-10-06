using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class RequestFriendEvent(IMessengerCommunicationService messenger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => messenger.RequestFriend(session, packet.ReadString());
}
