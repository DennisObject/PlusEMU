using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public sealed class MessengerMessageAckComposer(int confirmationId, int messageId, int createdAt) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.MessengerMessageAckComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(confirmationId);
        packet.WriteInteger(0); // Direct friend conversation.
        packet.WriteInteger(messageId);
        packet.WriteInteger(createdAt);
    }
}
