using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Marketplace;

public sealed record MarketplaceListing(uint FurniId, uint ItemId, int UserId, int AskingPrice, int TotalPrice, string PublicName, int SpriteId,
    string ItemType, DateTimeOffset ListedAt, string ExtraData, uint LimitedNumber, uint LimitedStack);

public sealed record MarketplaceSoldOffer(uint OfferId, int AskingPrice);

public interface IMarketplaceOfferStore
{
    // Writes the offer and removes the furni row in one transaction. Throws, with nothing committed, unless exactly one furni row was removed.
    void ListFurni(MarketplaceListing listing);

    // Claims the user's sold offers in one transaction. Returns the total owed, or null (nothing changed) when a claim is negative,
    // overflows, or accepts rejects the total.
    int? ClaimSold(int userId, Func<int, bool> accepts);
}

public sealed class MarketplaceOfferStore(IDatabase database) : IMarketplaceOfferStore
{
    public void ListFurni(MarketplaceListing listing)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try {
            connection.Execute("INSERT INTO `catalog_marketplace_offers` (`furni_id`,`item_id`,`user_id`,`asking_price`,`total_price`,`public_name`,`sprite_id`,`item_type`,`listed_at`,`extra_data`,`limited_number`,`limited_stack`) " +
                "VALUES (@FurniId,@ItemId,@UserId,@AskingPrice,@TotalPrice,@PublicName,@SpriteId,@ItemType,@ListedAt,@ExtraData,@LimitedNumber,@LimitedStack)",
                new
                {
                    listing.FurniId,
                    listing.ItemId,
                    listing.UserId,
                    listing.AskingPrice,
                    listing.TotalPrice,
                    listing.PublicName,
                    listing.SpriteId,
                    listing.ItemType,
                    ListedAt = listing.ListedAt.UtcDateTime,
                    listing.ExtraData,
                    listing.LimitedNumber,
                    listing.LimitedStack
                }, transaction);
            // TODO @80O: Do not delete items from the items table when putting on marketplace. Just reference the furniture instead.
            var removed = connection.Execute("DELETE FROM `items` WHERE `id` = @FurniId AND `user_id` = @UserId AND `room_id` = 0 LIMIT 1",
                new { listing.FurniId, listing.UserId }, transaction);

            if (removed != 1) {
                throw new InvalidOperationException($"Furni {listing.FurniId} was not removed from user {listing.UserId}; the offer was not published.");
            }

            transaction.Commit();
        }
        catch {
            transaction.Rollback();
            throw;
        }
    }

    public int? ClaimSold(int userId, Func<int, bool> accepts)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try {
            var sold = connection.Query<MarketplaceSoldOffer>("SELECT `offer_id` AS OfferId, `asking_price` AS AskingPrice FROM `catalog_marketplace_offers` " +
                "WHERE `user_id` = @userId AND `state` = '2' FOR UPDATE", new { userId }, transaction).ToList();
            long owed = 0;

            foreach (var offer in sold) {
                if (offer.AskingPrice < 0) {
                    transaction.Rollback();

                    return null;
                }

                owed += offer.AskingPrice;
            }

            if (owed > int.MaxValue || !accepts((int)owed)) {
                transaction.Rollback();

                return null;
            }

            if (sold.Count > 0) {
                var deleted = connection.Execute("DELETE FROM `catalog_marketplace_offers` WHERE `user_id` = @userId AND `state` = '2' AND `offer_id` IN @offerIds",
                    new { userId, offerIds = sold.Select(offer => offer.OfferId).ToArray() }, transaction);

                if (deleted != sold.Count) {
                    throw new InvalidOperationException($"Claimed {sold.Count} sold offers for user {userId} but removed {deleted}; nothing was paid.");
                }
            }

            transaction.Commit();

            return (int)owed;
        }
        catch {
            transaction.Rollback();
            throw;
        }
    }
}
