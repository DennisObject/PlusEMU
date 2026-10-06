using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Polls;

public class PollOfferComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PollOfferComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(111141); //Room Id
        packet.WriteString("CLIENT_NPS");
        packet.WriteString("Customer Satisfaction Poll");
        packet.WriteString("Give us your opinion!");
    }
}
