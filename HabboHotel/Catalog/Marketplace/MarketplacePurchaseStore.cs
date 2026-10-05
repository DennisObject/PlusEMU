using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Catalog.Marketplace;

public enum MarketplacePurchaseRefusal { NotFound, Sold, Expired, UnknownItem, OwnOffer, InsufficientCredits }

public sealed record MarketplacePurchaseRequest(int OfferId, int BuyerId, int BuyerCredits, double ExpiredBefore, Func<uint, ItemDefinition?> DefinitionOf);

public sealed record MarketplaceClaimedOffer(int TotalPrice, uint FurniId, uint ItemId, string ExtraData, uint LimitedNumber, uint LimitedStack, ItemDefinition Definition);

public sealed record MarketplacePurchaseResult(MarketplacePurchaseRefusal? Refusal, MarketplaceClaimedOffer? Offer);

public interface IMarketplacePurchaseStore
{
    // Claims one offer, delivers its furni row and records the sale in one transaction. Refusals change nothing.
    MarketplacePurchaseResult Claim(MarketplacePurchaseRequest request);
}

public sealed class MarketplacePurchaseStore(IDatabase database) : IMarketplacePurchaseStore
{
    // Column types as MariaDB stores them: user_id and item_id are unsigned, the limited columns signed.
    private sealed class OfferRow
    {
        public string State { get; set; } = "";
        public double Timestamp { get; set; }
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
        try
        {
            var offer = connection.QuerySingleOrDefault<OfferRow>("SELECT `state` AS State, `timestamp` AS Timestamp, `total_price` AS TotalPrice, `extra_data` AS ExtraData, " +
                "`item_id` AS ItemId, `furni_id` AS FurniId, `user_id` AS UserId, `limited_number` AS LimitedNumber, `limited_stack` AS LimitedStack " +
                "FROM `catalog_marketplace_offers` WHERE `offer_id` = @OfferId LIMIT 1 FOR UPDATE", new { request.OfferId }, transaction);
            if (offer == null) return Refuse(transaction, MarketplacePurchaseRefusal.NotFound);
            if (offer.State == "2") return Refuse(transaction, MarketplacePurchaseRefusal.Sold);
            if (request.ExpiredBefore > offer.Timestamp) return Refuse(transaction, MarketplacePurchaseRefusal.Expired);
            var definition = request.DefinitionOf(offer.ItemId);
            if (definition == null) return Refuse(transaction, MarketplacePurchaseRefusal.UnknownItem);
            if (offer.UserId == (uint)request.BuyerId) return Refuse(transaction, MarketplacePurchaseRefusal.OwnOffer);
            if (offer.TotalPrice > request.BuyerCredits) return Refuse(transaction, MarketplacePurchaseRefusal.InsufficientCredits);

            var sold = connection.Execute("UPDATE `catalog_marketplace_offers` SET `state` = '2' WHERE `offer_id` = @OfferId AND `state` = '1' LIMIT 1",
                new { request.OfferId }, transaction);
            if (sold != 1) throw new InvalidOperationException($"Offer {request.OfferId} changed while it was being bought; nothing was delivered.");

            // Delivery keeps the furni id the offer was listed under, so a second delivery of the same furni fails the primary key.
            var delivered = connection.Execute("INSERT INTO `items` (`id`,`base_item`,`user_id`,`room_id`,`x`,`y`,`z`,`wall_pos`,`rot`,`extra_data`,`limited_number`,`limited_stack`) " +
                "VALUES (@FurniId,@ItemId,@BuyerId,0,0,0,0,'',0,@ExtraData,@LimitedNumber,@LimitedStack)",
                new { offer.FurniId, offer.ItemId, BuyerId = request.BuyerId, offer.ExtraData, offer.LimitedNumber, offer.LimitedStack }, transaction);
            if (delivered != 1) throw new InvalidOperationException($"Furni {offer.FurniId} was not delivered; nothing was charged.");

            RecordSale(connection, transaction, definition.SpriteId, offer.TotalPrice);
            transaction.Commit();
            return new MarketplacePurchaseResult(null, new MarketplaceClaimedOffer(offer.TotalPrice, offer.FurniId, offer.ItemId, offer.ExtraData,
                Convert.ToUInt32(offer.LimitedNumber), Convert.ToUInt32(offer.LimitedStack), definition));
        }
        catch
        {
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
        if (id > 0)
            connection.Execute("UPDATE `catalog_marketplace_data` SET `sold` = `sold` + 1, `avgprice` = (`avgprice` + @totalPrice) WHERE `id` = @id LIMIT 1",
                new { totalPrice, id }, transaction);
        else
            connection.Execute("INSERT INTO `catalog_marketplace_data` (`sprite`, `sold`, `avgprice`) VALUES (@spriteId, 1, @totalPrice)",
                new { spriteId, totalPrice }, transaction);
    }
}
