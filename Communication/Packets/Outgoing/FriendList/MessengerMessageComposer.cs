using System.Globalization;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public sealed class MessengerMessageComposer(int messageId, int senderId, int habbiconId, int createdAt) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.MessengerMessageComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(0); // Direct friend conversation.
        packet.WriteInteger(messageId);
        packet.WriteInteger(senderId);
        packet.WriteInteger(4); // Habbicon message type.
        packet.WriteString(habbiconId.ToString(CultureInfo.InvariantCulture));
        packet.WriteString("");
        packet.WriteInteger(createdAt);
    }
}
