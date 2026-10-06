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
        packet.WriteInteger(0); //No idea.
        packet.WriteInteger(0); //No idea.
        packet.WriteInteger(_itemId);
        packet.WriteUInteger(_spriteId);
    }
}
