using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Marketplace;

public sealed record MarketplaceListing(uint FurniId, uint ItemId, int UserId, int AskingPrice, int TotalPrice, string PublicName, int SpriteId,
    string ItemType, double Timestamp, string ExtraData, uint LimitedNumber, uint LimitedStack);

public sealed record MarketplaceSoldOffer(uint OfferId, int AskingPrice);

public interface IMarketplaceOfferStore
{
    // Writes the offer and removes the furni row in one transaction; throws (and changes nothing) if either write fails.
    void ListFurni(MarketplaceListing listing);

    // Claims the user's sold offers in one transaction. Returns the total owed, or null (nothing changed) when accepts rejects it.
    int? ClaimSold(int userId, Func<int, bool> accepts);
}

public sealed class MarketplaceOfferStore(IDatabase database) : IMarketplaceOfferStore
{
    public void ListFurni(MarketplaceListing listing)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("INSERT INTO `catalog_marketplace_offers` (`furni_id`,`item_id`,`user_id`,`asking_price`,`total_price`,`public_name`,`sprite_id`,`item_type`,`timestamp`,`extra_data`,`limited_number`,`limited_stack`) " +
            "VALUES (@FurniId,@ItemId,@UserId,@AskingPrice,@TotalPrice,@PublicName,@SpriteId,@ItemType,@Timestamp,@ExtraData,@LimitedNumber,@LimitedStack)", listing, transaction);
        // TODO @80O: Do not delete items from the items table when putting on marketplace. Just reference the furniture instead.
        connection.Execute("DELETE FROM `items` WHERE `id` = @FurniId AND `user_id` = @UserId LIMIT 1", new { listing.FurniId, listing.UserId }, transaction);
        transaction.Commit();
    }

    public int? ClaimSold(int userId, Func<int, bool> accepts)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var sold = connection.Query<MarketplaceSoldOffer>("SELECT `offer_id` AS OfferId, `asking_price` AS AskingPrice FROM `catalog_marketplace_offers` " +
            "WHERE `user_id` = @userId AND `state` = '2' FOR UPDATE", new { userId }, transaction).ToList();
        var owed = sold.Sum(offer => (long)offer.AskingPrice);
        if (owed > int.MaxValue || !accepts((int)owed))
        {
            transaction.Rollback();
            return null;
        }
        if (sold.Count > 0)
            connection.Execute("DELETE FROM `catalog_marketplace_offers` WHERE `user_id` = @userId AND `state` = '2' AND `offer_id` IN @offerIds",
                new { userId, offerIds = sold.Select(offer => offer.OfferId).ToArray() }, transaction);
        transaction.Commit();
        return (int)owed;
    }
}
