using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Chat;

internal static class RoomChatPacket
{
    public static void Write(IOutgoingPacket packet, int virtualId, string message, int emotion, int colour)
    {
        packet.WriteInteger(virtualId);
        packet.WriteString(message);
        packet.WriteInteger(emotion);
        packet.WriteInteger(colour);
        packet.WriteInteger(0);
        packet.WriteString(string.Empty);
        packet.WriteInteger(message.Length);
        packet.WriteString(string.Empty);
        packet.WriteInteger(-1);
    }
}
