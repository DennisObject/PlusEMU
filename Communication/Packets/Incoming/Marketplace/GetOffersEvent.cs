using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class GetOffersEvent(IMarketplaceOfferSearchService search) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var minCost = packet.ReadInt();
        var maxCost = packet.ReadInt();
        var searchQuery = packet.ReadString();
        var filterMode = packet.ReadInt();
        session.Send(new MarketPlaceOffersComposer(search.Search(minCost, maxCost, searchQuery, filterMode)));
        return Task.CompletedTask;
    }
}
