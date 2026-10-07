using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Polls;

public sealed class SimplePollAnswersComposer(int questionId, int no, int yes) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.SimplePollAnswersComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(questionId);
        packet.WriteInteger(2);
        packet.WriteString("0");
        packet.WriteInteger(no);
        packet.WriteString("1");
        packet.WriteInteger(yes);
    }
}
