using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator.New;

// TODO @80O: Implement
public class NavigatorPreferencesComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.NavigatorPreferencesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(68); //X
        packet.WriteInteger(42); //Y
        packet.WriteInteger(425); //Width
        packet.WriteInteger(592); //Height
        packet.WriteBoolean(false); //Show or hide saved searches.
        packet.WriteInteger(0); // Results mode.
    }
}
