using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Catalog.Marketplace;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class GetOwnOffersEvent : IPacketEvent
{
    private readonly IMarketplaceManager _marketplaceManager;
    public GetOwnOffersEvent(IMarketplaceManager marketplaceManager) => _marketplaceManager = marketplaceManager;
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new MarketPlaceOwnOffersComposer(_marketplaceManager.OwnOffers(session.GetHabbo().Id)));

        return Task.CompletedTask;
    }
}
