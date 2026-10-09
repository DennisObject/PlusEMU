using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

public class MarketplaceItemStatsComposer : IServerPacket
{
    private readonly int _itemId;
    private readonly uint _spriteId;
    private readonly int _averagePrice;
    private readonly int _offerCount;
    public uint MessageId => ServerPacketHeader.MarketplaceItemStatsComposer;

    public MarketplaceItemStatsComposer(int itemId, uint spriteId, int averagePrice, int offerCount)
    {
        _itemId = itemId;
        _spriteId = spriteId;
        _averagePrice = averagePrice;
        _offerCount = offerCount;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_averagePrice); //Avg price in last 7 days.
        packet.WriteInteger(_offerCount);
        packet.WriteInteger(0); // History window in days; no daily history is supplied.
        packet.WriteInteger(0); // Daily history row count (day offset, average price, sold amount).
        packet.WriteInteger(_itemId);
        packet.WriteUInteger(_spriteId);
        packet.WriteInteger(0); // Lowest current price is unavailable in this snapshot.
        packet.WriteInteger(0); // Suggested price is unavailable in this snapshot.
    }
}
