using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Marketplace;

public enum MarketplacePurchaseOutcome { Bought, NotFound, Sold, Expired, UnknownItem, OwnOffer, InsufficientCredits, InvalidOffer, WalletClosed }

public interface IMarketplacePurchaseService
{
    MarketplacePurchaseOutcome Buy(GameClient session, int offerId);
}

public sealed class MarketplacePurchaseService(IMarketplacePurchaseStore store, IItemDataManager items, IMarketplaceManager marketplace,
    IMarketplaceOfferSearchService offers, TimeProvider time) : IMarketplacePurchaseService
{
    private const double OfferLifetimeSeconds = 172800;

    // One lock for every buyer's sale record: the averages and the sale-stat rows (which have no unique sprite key) update here only.
    // Static so every instance shares it, whatever the DI lifetime. Lock order is always buyer WalletSync, then this lock.
    private static readonly object Sales = new();

    public MarketplacePurchaseOutcome Buy(GameClient session, int offerId)
    {
        var habbo = session.GetHabbo();
        MarketplacePurchaseOutcome outcome;
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return MarketplacePurchaseOutcome.WalletClosed;
            var listedBefore = time.GetUtcNow().AddSeconds(-OfferLifetimeSeconds);
            MarketplacePurchaseResult result;
            lock (Sales)
            {
                result = store.Claim(new MarketplacePurchaseRequest(offerId, habbo.Id, habbo.Credits, listedBefore,
                    itemId => items.Items.TryGetValue(itemId, out var definition) ? definition : null,
                    delivery => PrepareDelivery(habbo, delivery)));
                if (result.Offer is { } sale)
                    RecordAverage(sale.SpriteId, sale.TotalPrice);
            }
            if (result.Refusal is { } refusal)
            {
                outcome = Outcome(refusal);
                Publish(session, outcome);
                return outcome;
            }
            var claim = result.Offer!;

            // The item was built before the commit; charging and handing it over cannot fail.
            habbo.Credits -= claim.TotalPrice;
            session.Send(new CreditBalanceComposer(habbo.Credits));
            habbo.Inventory.Furniture.AddItem(claim.Delivery);
            session.Send(new FurniListNotificationComposer(claim.Delivery.Id, 1));
            session.Send(new PurchaseOKComposer());
            session.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(claim.Delivery)));
            session.Send(new FurniListUpdateComposer());
            outcome = MarketplacePurchaseOutcome.Bought;
        }
        Publish(session, outcome);
        return outcome;
    }

    // Runs inside the claim, before its first write: a furni that cannot be built aborts the purchase with nothing charged or sold.
    private static InventoryItem PrepareDelivery(Habbo habbo, MarketplaceDelivery delivery)
    {
        var item = new Item
        {
            Id = delivery.FurniId, OwnerId = (uint)habbo.Id, Definition = delivery.Definition,
            ExtraData = FurniExtraData.Load(delivery.Definition, delivery.ExtraData, keepLegacy: true),
            UniqueNumber = delivery.LimitedNumber, UniqueSeries = delivery.LimitedStack,
        };
        return item.ToInventoryItem() ?? throw new InvalidOperationException($"Furni {delivery.FurniId} could not be built for delivery; nothing was charged.");
    }

    private void RecordAverage(int spriteId, int totalPrice)
    {
        if (marketplace.MarketAverages.ContainsKey(spriteId) && marketplace.MarketCounts.ContainsKey(spriteId))
        {
            var count = marketplace.MarketCounts[spriteId];
            var average = marketplace.MarketAverages[spriteId] += totalPrice;
            marketplace.MarketAverages.Remove(spriteId);
            marketplace.MarketAverages.Add(spriteId, average);
            marketplace.MarketCounts.Remove(spriteId);
            marketplace.MarketCounts.Add(spriteId, count + 1);
            return;
        }
        if (!marketplace.MarketAverages.ContainsKey(spriteId))
            marketplace.MarketAverages.Add(spriteId, totalPrice);
        if (!marketplace.MarketCounts.ContainsKey(spriteId))
            marketplace.MarketCounts.Add(spriteId, 1);
    }

    // Notices and the refreshed list the buyer sees, per outcome.
    private void Publish(GameClient session, MarketplacePurchaseOutcome outcome)
    {
        switch (outcome)
        {
            case MarketplacePurchaseOutcome.WalletClosed:
                return;
            case MarketplacePurchaseOutcome.OwnOffer:
                session.SendNotification("To prevent average boosting you cannot purchase your own marketplace offers.");
                return;
            case MarketplacePurchaseOutcome.InsufficientCredits:
                session.SendNotification("Oops, you do not have enough credits for this.");
                return;
            case MarketplacePurchaseOutcome.Sold:
                session.SendNotification("Oops, this offer is no longer available.");
                break;
            case MarketplacePurchaseOutcome.Expired:
                session.SendNotification("Oops, this offer has expired..");
                break;
            case MarketplacePurchaseOutcome.UnknownItem:
                session.SendNotification("Item isn't in the hotel anymore.");
                break;
        }
        session.Send(new MarketPlaceOffersComposer(offers.Search(-1, -1, "", 1)));
    }

    private static MarketplacePurchaseOutcome Outcome(MarketplacePurchaseRefusal refusal) => refusal switch
    {
        MarketplacePurchaseRefusal.NotFound => MarketplacePurchaseOutcome.NotFound,
        MarketplacePurchaseRefusal.Sold => MarketplacePurchaseOutcome.Sold,
        MarketplacePurchaseRefusal.Expired => MarketplacePurchaseOutcome.Expired,
        MarketplacePurchaseRefusal.UnknownItem => MarketplacePurchaseOutcome.UnknownItem,
        MarketplacePurchaseRefusal.OwnOffer => MarketplacePurchaseOutcome.OwnOffer,
        MarketplacePurchaseRefusal.InvalidOffer => MarketplacePurchaseOutcome.InvalidOffer,
        _ => MarketplacePurchaseOutcome.InsufficientCredits,
    };
}
