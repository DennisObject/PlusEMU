using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Marketplace;

public enum MarketplacePurchaseOutcome { Bought, NotFound, Sold, Expired, UnknownItem, OwnOffer, InsufficientCredits, WalletClosed }

public interface IMarketplacePurchaseService
{
    MarketplacePurchaseOutcome Buy(GameClient session, int offerId);
}

public sealed class MarketplacePurchaseService(IMarketplacePurchaseStore store, IItemDataManager items, IMarketplaceManager marketplace, TimeProvider time) : IMarketplacePurchaseService
{
    private const double OfferLifetimeSeconds = 172800;

    public MarketplacePurchaseOutcome Buy(GameClient session, int offerId)
    {
        var habbo = session.GetHabbo();
        // Charging, delivery and the in-memory averages stay under the buyer's wallet lock, so one wallet cannot be charged twice concurrently.
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return MarketplacePurchaseOutcome.WalletClosed;
            var expiredBefore = time.GetUtcNow().ToUnixTimeSeconds() - OfferLifetimeSeconds;
            var result = store.Claim(new MarketplacePurchaseRequest(offerId, habbo.Id, habbo.Credits, expiredBefore,
                itemId => items.Items.TryGetValue(itemId, out var definition) ? definition : null));
            if (result.Refusal is { } refusal)
                return Outcome(refusal);
            var claim = result.Offer!;

            habbo.Credits -= claim.TotalPrice;
            session.Send(new CreditBalanceComposer(habbo.Credits));
            var giveItem = new Item
            {
                Id = claim.FurniId, OwnerId = (uint)habbo.Id, Definition = claim.Definition,
                ExtraData = FurniExtraData.Load(claim.Definition, claim.ExtraData, keepLegacy: true),
                UniqueNumber = claim.LimitedNumber, UniqueSeries = claim.LimitedStack,
            }.ToInventoryItem();
            if (giveItem != null)
            {
                habbo.Inventory.Furniture.AddItem(giveItem);
                session.Send(new FurniListNotificationComposer(giveItem.Id, 1));
                session.Send(new PurchaseOKComposer());
                session.Send(new FurniListAddComposer(giveItem));
                session.Send(new FurniListUpdateComposer());
            }
            RecordAverage(claim.Definition.SpriteId, claim.TotalPrice);
            return MarketplacePurchaseOutcome.Bought;
        }
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

    private static MarketplacePurchaseOutcome Outcome(MarketplacePurchaseRefusal refusal) => refusal switch
    {
        MarketplacePurchaseRefusal.NotFound => MarketplacePurchaseOutcome.NotFound,
        MarketplacePurchaseRefusal.Sold => MarketplacePurchaseOutcome.Sold,
        MarketplacePurchaseRefusal.Expired => MarketplacePurchaseOutcome.Expired,
        MarketplacePurchaseRefusal.UnknownItem => MarketplacePurchaseOutcome.UnknownItem,
        MarketplacePurchaseRefusal.OwnOffer => MarketplacePurchaseOutcome.OwnOffer,
        _ => MarketplacePurchaseOutcome.InsufficientCredits,
    };
}
