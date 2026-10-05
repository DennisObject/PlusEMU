using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class SendMsgEvent(IMessengerCommunicationService messenger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var text = packet.ReadString();
        return messenger.SendMessage(session, userId, text);
    }
}