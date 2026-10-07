using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.Communication.Packets.Outgoing.Rooms.Polls;

public sealed class SimplePollStartComposer(RoomWordQuizSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.SimplePollStartComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(snapshot.Question);
        packet.WriteInteger(0);
        packet.WriteInteger(snapshot.QuestionId);
        packet.WriteInteger(snapshot.DurationMilliseconds);
        packet.WriteInteger(snapshot.QuestionId);
        packet.WriteInteger(0);
        packet.WriteInteger(3);
        packet.WriteString(snapshot.Question);
    }
}
