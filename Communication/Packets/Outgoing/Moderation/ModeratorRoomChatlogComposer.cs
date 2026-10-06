using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class ModeratorRoomChatlogComposer(ModeratorRoomChatlog chatlog) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorRoomChatlogComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteByte(1);
        packet.WriteShort(2);
        packet.WriteString("roomName");
        packet.WriteByte(2);
        packet.WriteString(chatlog.Room.Name);
        packet.WriteString("roomId");
        packet.WriteByte(1);
        packet.WriteUInteger(chatlog.Room.Id);
        packet.WriteShort((short)chatlog.Entries.Length);

        foreach (var entry in chatlog.Entries) {
            packet.WriteString(entry.Timestamp.DateTime.ToShortTimeString());
            packet.WriteInteger(entry.UserId);
            packet.WriteString(entry.Username);
            packet.WriteString(!string.IsNullOrEmpty(entry.Message) ? entry.Message : "** user sent a blank message **");
            packet.WriteBoolean(false);
        }
    }
}
