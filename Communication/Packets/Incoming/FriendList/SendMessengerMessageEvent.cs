using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

// The modern client sends Habicons through this header. Legacy text still uses SendMsgEvent.
public sealed class SendMessengerMessageEvent(IHabbiconMessengerService messenger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int conversationId = packet.ReadInt(), recipientId = packet.ReadInt(), confirmationId = packet.ReadInt(), type = packet.ReadInt();
        string message = packet.ReadString(), metadata = packet.ReadString();
        messenger.Send(session, conversationId, recipientId, confirmationId, type, message, metadata);
        return Task.CompletedTask;
    }
}
