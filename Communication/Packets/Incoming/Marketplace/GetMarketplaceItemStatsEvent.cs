using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class GetMarketplaceItemStatsEvent(IMarketplaceManager marketplace) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadInt();
        var spriteId = packet.ReadUInt();
        var stats = marketplace.ItemStats(spriteId);
        session.Send(new MarketplaceItemStatsComposer(itemId, spriteId, stats.AveragePrice, stats.OfferCount));

        return Task.CompletedTask;
    }
}
