using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

// MarketplaceConfigurationEvent of the official client (WIN63-202609161723): enabled, commission, token batch price and size, minimum and maximum price, expiration hours,
// average price period, then sellingFeePercentage, revenueLimit and halfTaxLimit.
public class MarketplaceConfigurationComposer(IMarketplaceFeePolicy fee) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.MarketplaceConfigurationComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true);
        packet.WriteInteger(fee.SellingFeePercentage); // Commission percentage.
        packet.WriteInteger(0); // Credits required to buy listing advertisements.
        packet.WriteInteger(0); // Listing advertisements granted by that purchase.
        packet.WriteInteger(1);
        packet.WriteInteger(99999999); //Max price.
        packet.WriteInteger(48);
        packet.WriteInteger(7); //Days.
        packet.WriteInteger(fee.SellingFeePercentage);
        packet.WriteInteger(fee.RevenueLimit);
        packet.WriteInteger(fee.HalfTaxLimit);
    }
}
