using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class ModeratorUserChatlogComposer(ModeratorUserChatlog chatlog) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorUserChatlogComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(chatlog.User.Id);
        packet.WriteString(chatlog.User.Username);
        packet.WriteInteger(chatlog.Rooms.Length);

        foreach (var room in chatlog.Rooms) {
            packet.WriteByte(1);
            packet.WriteShort(2);
            packet.WriteString("roomName");
            packet.WriteByte(2);
            packet.WriteString(room.Room.Name);
            packet.WriteString("roomId");
            packet.WriteByte(1);
            packet.WriteUInteger(room.Room.Id);
            packet.WriteShort((short)room.Entries.Length);

            foreach (var entry in room.Entries) {
                packet.WriteString(entry.Timestamp.DateTime.ToShortTimeString());
                packet.WriteInteger(entry.UserId);
                packet.WriteString(entry.Username);
                packet.WriteString(!string.IsNullOrEmpty(entry.Message) ? entry.Message : "** user sent a blank message **");
                packet.WriteBoolean(chatlog.User.Id == entry.UserId);
            }
        }
    }
}
