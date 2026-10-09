using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class MarketplaceConfigurationComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.MarketplaceConfigurationComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true);
        packet.WriteInteger(1); // Commission percentage.
        packet.WriteInteger(0); // Credits required to buy listing advertisements.
        packet.WriteInteger(0); // Listing advertisements granted by that purchase.
        packet.WriteInteger(1);
        packet.WriteInteger(99999999); //Max price.
        packet.WriteInteger(48);
        packet.WriteInteger(7); //Days.
    }
}
