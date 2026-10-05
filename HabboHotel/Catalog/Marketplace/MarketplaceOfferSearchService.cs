using System.Collections.Immutable;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Marketplace;

public sealed record MarketplaceOfferEntry(uint OfferId, uint SpriteId, uint LimitedNumber, uint LimitedStack, int TotalPrice, int AveragePrice, int Count);

public sealed record MarketplaceOffersSnapshot(ImmutableArray<MarketplaceOfferEntry> Offers);

public interface IMarketplaceOfferSearchService
{
    MarketplaceOffersSnapshot Search(int minCost, int maxCost, string searchQuery, int filterMode);
    MarketplaceOffersSnapshot Capture(IReadOnlyDictionary<uint, MarketOffer> best, IReadOnlyDictionary<uint, int> counts);
}

public sealed record MarketplaceOfferRow(uint OfferId, string ItemType, int SpriteId, int TotalPrice, int LimitedNumber, int LimitedStack);

public sealed class MarketplaceOfferSearchService(IDatabase database, IMarketplaceManager marketplace, TimeProvider time) : IMarketplaceOfferSearchService
{
    // Only the fixed ORDER BY texts below ever reach the SQL; filters, timestamp threshold and limit are bound.
    private const string SelectOffers = "SELECT `offer_id` AS OfferId, `item_type` AS ItemType, `sprite_id` AS SpriteId, `total_price` AS TotalPrice, " +
        "`limited_number` AS LimitedNumber, `limited_stack` AS LimitedStack FROM `catalog_marketplace_offers` " +
        "WHERE `state` = '1' AND `timestamp` >= @threshold AND (@minCost < 0 OR `total_price` > @minCost) AND (@maxCost < 0 OR `total_price` < @maxCost) ";
    private const string NewestFirst = SelectOffers + "ORDER BY `asking_price` DESC LIMIT 500";
    private const string CheapestFirst = SelectOffers + "ORDER BY `asking_price` ASC LIMIT 500";

    public MarketplaceOffersSnapshot Search(int minCost, int maxCost, string searchQuery, int filterMode)
    {
        var sql = filterMode == 1 ? NewestFirst : CheapestFirst;
        // Same cutoff as the legacy FormatTimestampString: two days back on the server's local clock.
        var threshold = (time.GetLocalNow().DateTime - new DateTime(1970, 1, 1)).TotalSeconds - 172800.0;
        List<MarketplaceOfferRow> rows;
        using (var connection = database.Connection())
            rows = connection.Query<MarketplaceOfferRow>(sql, new { threshold, minCost, maxCost }).ToList();
        marketplace.MarketItems.Clear();
        marketplace.MarketItemKeys.Clear();
        foreach (var row in rows)
        {
            if (!marketplace.MarketItemKeys.Contains(Convert.ToInt32(row.OfferId)))
            {
                marketplace.MarketItemKeys.Add(Convert.ToInt32(row.OfferId));
                marketplace.MarketItems.Add(new(Convert.ToUInt32(row.OfferId), Convert.ToUInt32(row.SpriteId),
                    row.TotalPrice, int.Parse(row.ItemType), Convert.ToUInt32(row.LimitedNumber), Convert.ToUInt32(row.LimitedStack)));
            }
        }
        /// TODO @80O: Wtf is this shit
        var best = new Dictionary<uint, MarketOffer>();
        var counts = new Dictionary<uint, int>();
        foreach (var item in marketplace.MarketItems)
        {
            if (best.ContainsKey(item.SpriteId))
            {
                if (item.LimitedNumber > 0)
                {
                    if (!best.ContainsKey(item.OfferId))
                        best.Add(item.OfferId, item);
                    if (!counts.ContainsKey(item.OfferId))
                        counts.Add(item.OfferId, 1);
                }
                else
                {
                    if (best[item.SpriteId].TotalPrice > item.TotalPrice)
                    {
                        best.Remove(item.SpriteId);
                        best.Add(item.SpriteId, item);
                    }
                    var num = counts[item.SpriteId];
                    counts.Remove(item.SpriteId);
                    counts.Add(item.SpriteId, num + 1);
                }
            }
            else
            {
                if (!best.ContainsKey(item.SpriteId))
                    best.Add(item.SpriteId, item);
                if (!counts.ContainsKey(item.SpriteId))
                    counts.Add(item.SpriteId, 1);
            }
        }
        return Capture(best, counts);
    }

    public MarketplaceOffersSnapshot Capture(IReadOnlyDictionary<uint, MarketOffer> best, IReadOnlyDictionary<uint, int> counts) => new(
        best.Values.Select(offer => new MarketplaceOfferEntry(offer.OfferId, offer.SpriteId, offer.LimitedNumber, offer.LimitedStack, offer.TotalPrice,
            marketplace.AvgPriceForSprite((int)offer.SpriteId), counts[offer.SpriteId])).ToImmutableArray());
}
