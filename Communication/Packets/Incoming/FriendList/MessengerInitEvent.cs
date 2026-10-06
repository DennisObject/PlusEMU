using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class MessengerInitEvent(IMessengerPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => presentation.ShowFriendList(session);
}
