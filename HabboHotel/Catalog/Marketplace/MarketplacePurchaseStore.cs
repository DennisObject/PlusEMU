using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Marketplace;

public enum MarketplacePurchaseRefusal
{
    NotFound, Sold, Expired, UnknownItem, OwnOffer, InsufficientCredits, InvalidOffer
}

// Everything the delivered furni needs, already converted to its unsigned wire types.
public sealed record MarketplaceDelivery(uint FurniId, uint ItemId, string ExtraData, uint LimitedNumber, uint LimitedStack, ItemDefinition Definition);

public sealed record MarketplacePurchaseRequest(int OfferId, int BuyerId, int BuyerCredits, DateTimeOffset ListedBefore,
    Func<uint, ItemDefinition?> DefinitionOf, Func<MarketplaceDelivery, InventoryItem> Prepare);

public sealed record MarketplaceClaimedOffer(int TotalPrice, int SpriteId, InventoryItem Delivery);

public sealed record MarketplacePurchaseResult(MarketplacePurchaseRefusal? Refusal, MarketplaceClaimedOffer? Offer);

public interface IMarketplacePurchaseStore
{
    // Claims one offer, delivers its furni row and records the sale in one transaction. Refusals and failures before the first write change nothing.
    MarketplacePurchaseResult Claim(MarketplacePurchaseRequest request);
}

public sealed class MarketplacePurchaseStore(IDatabase database) : IMarketplacePurchaseStore
{
    // Column types as MariaDB stores them: user_id and item_id are unsigned, the limited columns signed.
    private sealed class OfferRow
    {
        public string State { get; set; } = "";
        public DateTimeOffset? ListedAt { get; set; }
        public int TotalPrice { get; set; }
        public string ExtraData { get; set; } = "";
        public uint ItemId { get; set; }
        public uint FurniId { get; set; }
        public uint UserId { get; set; }
        public int LimitedNumber { get; set; }
        public int LimitedStack { get; set; }
    }

    public MarketplacePurchaseResult Claim(MarketplacePurchaseRequest request)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try {
            var offer = connection.QuerySingleOrDefault<OfferRow>("SELECT `state` AS State, `listed_at` AS ListedAt, `total_price` AS TotalPrice, `extra_data` AS ExtraData, " +
                "`item_id` AS ItemId, `furni_id` AS FurniId, `user_id` AS UserId, `limited_number` AS LimitedNumber, `limited_stack` AS LimitedStack " +
                "FROM `catalog_marketplace_offers` WHERE `offer_id` = @OfferId LIMIT 1 FOR UPDATE", new { request.OfferId }, transaction);

            if (offer == null) {
                return Refuse(transaction, MarketplacePurchaseRefusal.NotFound);
            }

            // Only a listed offer ('1') can be bought; any other state is treated as no longer available.
            if (offer.State != "1") {
                return Refuse(transaction, MarketplacePurchaseRefusal.Sold);
            }

            // A NULL listing time is unknown, so it cannot be proven live and is treated as expired.
            if (offer.ListedAt is not { } listedAt || listedAt < request.ListedBefore) {
                return Refuse(transaction, MarketplacePurchaseRefusal.Expired);
            }

            var definition = request.DefinitionOf(offer.ItemId);

            if (definition == null) {
                return Refuse(transaction, MarketplacePurchaseRefusal.UnknownItem);
            }

            if (offer.UserId == (uint)request.BuyerId) {
                return Refuse(transaction, MarketplacePurchaseRefusal.OwnOffer);
            }

            // A negative price would credit the buyer, and the limited columns are unsigned in the delivered item.
            if (offer.TotalPrice <= 0 || offer.LimitedNumber < 0 || offer.LimitedStack < 0) {
                return Refuse(transaction, MarketplacePurchaseRefusal.InvalidOffer);
            }

            if (offer.TotalPrice > request.BuyerCredits) {
                return Refuse(transaction, MarketplacePurchaseRefusal.InsufficientCredits);
            }

            // Everything that can fail on the way to delivery runs before the first write: parsing the furni data, building the inventory item.
            var delivery = new MarketplaceDelivery(offer.FurniId, offer.ItemId, offer.ExtraData,
                Convert.ToUInt32(offer.LimitedNumber), Convert.ToUInt32(offer.LimitedStack), definition);
            var prepared = request.Prepare(delivery);

            var sold = connection.Execute("UPDATE `catalog_marketplace_offers` SET `state` = '2' WHERE `offer_id` = @OfferId AND `state` = '1' LIMIT 1",
                new { request.OfferId }, transaction);

            if (sold != 1) {
                throw new InvalidOperationException($"Offer {request.OfferId} changed while it was being bought; nothing was delivered.");
            }

            // Delivery keeps the furni id the offer was listed under, so a second delivery of the same furni fails the primary key.
            var delivered = connection.Execute("INSERT INTO `items` (`id`,`base_item`,`user_id`,`room_id`,`x`,`y`,`z`,`wall_pos`,`rot`,`extra_data`,`limited_number`,`limited_stack`) " +
                "VALUES (@FurniId,@ItemId,@BuyerId,0,0,0,0,'',0,@ExtraData,@LimitedNumber,@LimitedStack)",
                new { delivery.FurniId, delivery.ItemId, BuyerId = request.BuyerId, delivery.ExtraData, delivery.LimitedNumber, delivery.LimitedStack }, transaction);

            if (delivered != 1) {
                throw new InvalidOperationException($"Furni {offer.FurniId} was not delivered; nothing was charged.");
            }

            RecordSale(connection, transaction, definition.SpriteId, offer.TotalPrice);
            transaction.Commit();

            return new MarketplacePurchaseResult(null, new MarketplaceClaimedOffer(offer.TotalPrice, definition.SpriteId, prepared));
        }
        catch {
            transaction.Rollback();
            throw;
        }
    }

    private static MarketplacePurchaseResult Refuse(System.Data.IDbTransaction transaction, MarketplacePurchaseRefusal refusal)
    {
        transaction.Rollback();

        return new MarketplacePurchaseResult(refusal, null);
    }

    private static void RecordSale(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int spriteId, int totalPrice)
    {
        var id = connection.QuerySingleOrDefault<int?>("SELECT `id` FROM `catalog_marketplace_data` WHERE `sprite` = @spriteId LIMIT 1", new { spriteId }, transaction) ?? 0;

        if (id > 0) {
            connection.Execute("UPDATE `catalog_marketplace_data` SET `sold` = `sold` + 1, `avgprice` = (`avgprice` + @totalPrice) WHERE `id` = @id LIMIT 1",
                new { totalPrice, id }, transaction);
        }
        else {
            connection.Execute("INSERT INTO `catalog_marketplace_data` (`sprite`, `sold`, `avgprice`) VALUES (@spriteId, 1, @totalPrice)",
                new { spriteId, totalPrice }, transaction);
        }
    }
}
