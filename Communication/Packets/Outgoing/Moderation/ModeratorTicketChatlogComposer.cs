using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public sealed class ModeratorTicketChatlogComposer(ModeratorTicketChatlogSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorTicketChatlogComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(data.TicketId);
        packet.WriteInteger(data.SenderId);
        packet.WriteInteger(data.ReportedId);
        packet.WriteUInteger(data.RoomId);
        packet.WriteByte(1);
        packet.WriteShort(2);
        packet.WriteString("roomName");
        packet.WriteByte(2);
        packet.WriteString(data.RoomName);
        packet.WriteString("roomId");
        packet.WriteByte(1);
        packet.WriteUInteger(data.RoomId);
        packet.WriteShort((short)data.Chats.Length);

        foreach (var chat in data.Chats)
        {
            packet.WriteString(data.CreatedAt.UtcDateTime.ToShortTimeString());
            packet.WriteInteger(data.TicketId);
            packet.WriteString(data.ReportedName);
            packet.WriteString(chat);
            packet.WriteBoolean(false);
        }
    }
}
