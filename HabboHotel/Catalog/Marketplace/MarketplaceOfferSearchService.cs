using System.Collections.Immutable;
using System.Data;
using System.Text;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Marketplace;

public sealed record MarketplaceOfferEntry(uint OfferId, uint SpriteId, uint LimitedNumber, uint LimitedStack, int TotalPrice, int AveragePrice, int Count);

public sealed record MarketplaceOffersSnapshot(ImmutableArray<MarketplaceOfferEntry> Offers);

public interface IMarketplaceOfferSearchService
{
    MarketplaceOffersSnapshot Search(int minCost, int maxCost, string searchQuery, int filterMode);
    MarketplaceOffersSnapshot Capture(IReadOnlyDictionary<uint, MarketOffer> best, IReadOnlyDictionary<uint, int> counts);
}

public sealed class MarketplaceOfferSearchService(IDatabase database, IMarketplaceManager marketplace) : IMarketplaceOfferSearchService
{
    public MarketplaceOffersSnapshot Search(int minCost, int maxCost, string searchQuery, int filterMode)
    {
        var builder = new StringBuilder();
        builder.Append($"WHERE `state` = '1' AND `timestamp` >= {marketplace.FormatTimestampString()}");
        if (minCost >= 0)
            builder.Append($" AND `total_price` > {minCost}");
        if (maxCost >= 0)
            builder.Append($" AND `total_price` < {maxCost}");
        var order = filterMode == 1 ? "ORDER BY `asking_price` DESC" : "ORDER BY `asking_price` ASC";
        DataTable? table;
        using (var dbClient = database.GetQueryReactor())
        {
            dbClient.SetQuery($"SELECT `offer_id`, `item_type`, `sprite_id`, `total_price`, `limited_number`,`limited_stack` FROM `catalog_marketplace_offers` {builder} {order} LIMIT 500");
            dbClient.AddParameter("search_query", $"%{searchQuery}%");
            table = dbClient.GetTable();
        }
        marketplace.MarketItems.Clear();
        marketplace.MarketItemKeys.Clear();
        if (table != null)
        {
            foreach (DataRow row in table.Rows)
            {
                if (!marketplace.MarketItemKeys.Contains(Convert.ToInt32(row["offer_id"])))
                {
                    marketplace.MarketItemKeys.Add(Convert.ToInt32(row["offer_id"]));
                    marketplace.MarketItems.Add(new(Convert.ToUInt32(row["offer_id"]), Convert.ToUInt32(row["sprite_id"]),
                        Convert.ToInt32(row["total_price"]), int.Parse(row["item_type"].ToString()), Convert.ToUInt32(row["limited_number"]), Convert.ToUInt32(row["limited_stack"])));
                }
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
