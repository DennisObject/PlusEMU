using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Polls;

public sealed class SimplePollAnswerComposer(int userId, string answer, int no, int yes) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.SimplePollAnswerComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(userId);
        packet.WriteString(answer);
        packet.WriteInteger(2);
        packet.WriteString("0");
        packet.WriteInteger(no);
        packet.WriteString("1");
        packet.WriteInteger(yes);
    }
}
