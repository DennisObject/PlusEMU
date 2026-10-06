using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogItemDiscountComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CatalogItemDiscountComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(100); //Most you can get.
        packet.WriteInteger(6);
        packet.WriteInteger(1);
        packet.WriteInteger(1);
        packet.WriteInteger(2); //Count
        {
            packet.WriteInteger(40);
            packet.WriteInteger(99);
        }
    }
}
