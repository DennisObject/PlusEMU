using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class BuyOfferEvent(IMarketplacePurchaseService purchases, IMarketplaceOfferSearchService offers) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var offerId = packet.ReadInt();
        var outcome = purchases.Buy(session, offerId);
        switch (outcome)
        {
            case MarketplacePurchaseOutcome.Sold:
                session.SendNotification("Oops, this offer is no longer available.");
                break;
            case MarketplacePurchaseOutcome.Expired:
                session.SendNotification("Oops, this offer has expired..");
                break;
            case MarketplacePurchaseOutcome.UnknownItem:
                session.SendNotification("Item isn't in the hotel anymore.");
                break;
            case MarketplacePurchaseOutcome.OwnOffer:
                session.SendNotification("To prevent average boosting you cannot purchase your own marketplace offers.");
                return Task.CompletedTask;
            case MarketplacePurchaseOutcome.InsufficientCredits:
                session.SendNotification("Oops, you do not have enough credits for this.");
                return Task.CompletedTask;
            case MarketplacePurchaseOutcome.WalletClosed:
                return Task.CompletedTask;
        }
        session.Send(new MarketPlaceOffersComposer(offers.Search(-1, -1, "", 1)));
        return Task.CompletedTask;
    }
}
