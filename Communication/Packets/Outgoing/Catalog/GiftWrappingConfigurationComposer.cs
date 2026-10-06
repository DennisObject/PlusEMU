using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

// TODO @80O: Implement / Configurable from database
public class GiftWrappingConfigurationComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GiftWrappingConfigurationComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true);
        packet.WriteInteger(1);
        packet.WriteInteger(10);

        for (var i = 3372; i < 3382;)
        {
            packet.WriteInteger(i);
            i++;
        }

        packet.WriteInteger(7);
        packet.WriteInteger(0);
        packet.WriteInteger(1);
        packet.WriteInteger(2);
        packet.WriteInteger(3);
        packet.WriteInteger(4);
        packet.WriteInteger(5);
        packet.WriteInteger(6);
        packet.WriteInteger(11);
        packet.WriteInteger(0);
        packet.WriteInteger(1);
        packet.WriteInteger(2);
        packet.WriteInteger(3);
        packet.WriteInteger(4);
        packet.WriteInteger(5);
        packet.WriteInteger(6);
        packet.WriteInteger(7);
        packet.WriteInteger(8);
        packet.WriteInteger(9);
        packet.WriteInteger(10);
        packet.WriteInteger(7);

        for (var i = 187; i < 194;)
        {
            packet.WriteInteger(i);
            i++;
        }
    }
}
