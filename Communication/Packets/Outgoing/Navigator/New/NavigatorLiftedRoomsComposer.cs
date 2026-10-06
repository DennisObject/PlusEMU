using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator.New;

// TODO @80O: Implement
public class NavigatorLiftedRoomsComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.NavigatorLiftedRoomsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(0); //Count
        {
            packet.WriteInteger(1); //Flat Id
            packet.WriteInteger(0); //Unknown
            packet.WriteString(string.Empty); //Image
            packet.WriteString("Caption"); //Caption.
        }
    }
}
