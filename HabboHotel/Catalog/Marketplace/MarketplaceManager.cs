using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.HabboHotel.Catalog.Marketplace;

public class MarketplaceManager : IMarketplaceManager
{
    private readonly IDatabase _database;
    private readonly IItemDataManager _itemDataManager;
    private readonly IItemFactory _itemFactory;
    public Dictionary<int, int> MarketAverages { get; } = new();
    public Dictionary<int, int> MarketCounts { get; } = new();
    public List<int> MarketItemKeys { get; } = new();
    public List<MarketOffer> MarketItems { get; } = new();

    public MarketplaceManager(IDatabase database, IItemDataManager itemDataManager, IItemFactory itemFactory)
    {
        _database = database;
        _itemDataManager = itemDataManager;
        _itemFactory = itemFactory;
    }
    public int AvgPriceForSprite(int spriteId)
    {
        var num = 0;
        var num2 = 0;
        if (MarketAverages.ContainsKey(spriteId) && MarketCounts.ContainsKey(spriteId))
        {
            if (MarketCounts[spriteId] > 0) return MarketAverages[spriteId] / MarketCounts[spriteId];
            return 0;
        }
        using var connection = _database.Connection();
        var totals = connection.QuerySingleOrDefault<MarketTotals>(
            "SELECT avgprice AS AveragePrice,sold AS Sold FROM catalog_marketplace_data WHERE sprite=@spriteId LIMIT 1", new { spriteId });
        num = totals?.AveragePrice ?? 0;
        num2 = totals?.Sold ?? 0;
        MarketAverages.Add(spriteId, num);
        MarketCounts.Add(spriteId, num2);
        if (num2 > 0)
            return Convert.ToInt32(Math.Ceiling((double)(num / num2)));
        return 0;
    }

    public string FormatTimestampString()
    {
        return FormatTimestamp().ToString().Split(new[] { ',' })[0];
    }

    public double FormatTimestamp() => UnixTimestamp.GetNow() - 172800.0;

    public int OfferCountForSprite(uint spriteId)
    {
        var dictionary = new Dictionary<uint, MarketOffer>();
        var dictionary2 = new Dictionary<uint, int>();
        foreach (var item in MarketItems)
        {
            if (dictionary.ContainsKey(item.SpriteId))
            {
                if (dictionary[item.SpriteId].TotalPrice > item.TotalPrice)
                {
                    dictionary.Remove(item.SpriteId);
                    dictionary.Add(item.SpriteId, item);
                }
                var num = dictionary2[item.SpriteId];
                dictionary2.Remove(item.SpriteId);
                dictionary2.Add(item.SpriteId, num + 1);
            }
            else
            {
                dictionary.Add(item.SpriteId, item);
                dictionary2.Add(item.SpriteId, 1);
            }
        }
        if (dictionary2.ContainsKey(spriteId)) return dictionary2[spriteId];
        return 0;
    }

    public MarketplaceItemStats ItemStats(uint spriteId)
    {
        using var connection = _database.Connection();
        var averagePrice = connection.ExecuteScalar<int?>(
            "SELECT `avgprice` FROM `catalog_marketplace_data` WHERE `sprite` = @spriteId LIMIT 1", new { spriteId }) ?? 0;
        return new(averagePrice, OfferCountForSprite(spriteId));
    }

    public MarketplaceOwnOffers OwnOffers(int userId)
    {
        using var connection = _database.Connection();
        var rows = connection.Query<OwnOfferRow>(
            "SELECT `timestamp` AS Timestamp, `state` AS State, `offer_id` AS OfferId, `sprite_id` AS SpriteId, `total_price` AS TotalPrice, `limited_number` AS LimitedNumber, `limited_stack` AS LimitedStack FROM `catalog_marketplace_offers` WHERE `user_id` = @userId",
            new { userId }).ToArray();
        var accumulated = connection.ExecuteScalar<int?>(
            "SELECT SUM(`asking_price`) FROM `catalog_marketplace_offers` WHERE `state` = 2 AND `user_id` = @userId",
            new { userId }) ?? 0;
        var now = UnixTimestamp.GetNow();
        var offers = rows.Select(row =>
        {
            var minutes = Convert.ToInt32(Math.Floor((row.Timestamp + 172800.0 - now) / 60.0));
            var state = int.Parse(row.State);
            if (minutes <= 0 && state != 2) { state = 3; minutes = 0; }
            return new MarketplaceOwnOffer(checked((int)row.OfferId), state, row.SpriteId, row.LimitedNumber, row.LimitedStack, row.TotalPrice, minutes);
        }).ToArray();
        return new(accumulated, offers);
    }

    private sealed class OwnOfferRow
    {
        public double Timestamp { get; set; }
        public string State { get; set; } = string.Empty;
        public uint OfferId { get; set; }
        public int SpriteId { get; set; }
        public int TotalPrice { get; set; }
        public int LimitedNumber { get; set; }
        public int LimitedStack { get; set; }
    }

    public int CalculateComissionPrice(float price) => Convert.ToInt32(Math.Ceiling(price / 100 * 1));

    public async Task<bool> TryCancelOffer(Habbo habbo, uint offerId)
    {
        var offer = await GetOffer(offerId);
        if (offer?.UserId != habbo.Id) return false;

        _itemDataManager.Items.TryGetValue(offer.ItemId, out var item);
        if (item == null) return false;

        var giveItem = _itemFactory.CreateSingleItem(item, habbo, offer.ExtraData, offer.ExtraData, offer.FurniId,offer.LimitedNumber, offer.LimitedStack);
        habbo.Client.Send(new FurniListNotificationComposer(giveItem.Id, 1));
        habbo.Client.Send(new FurniListUpdateComposer());
        await DeleteOffer(offerId);
        return true;
    }

    public async Task<MarketOffer?> GetOffer(uint offerId)
    {
        using var connection = _database.Connection();
        return await connection.QuerySingleOrDefaultAsync<MarketOffer>(
            "SELECT `furni_id`, `sprite_id`, `item_id`, `user_id`, `extra_data`, `offer_id`, `state`, `timestamp`, `limited_number`, `limited_stack` FROM `catalog_marketplace_offers` WHERE `offer_id` = @offerId LIMIT 1",
            new { offerId });
    }

    public async Task DeleteOffer(uint offerId)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("DELETE FROM `catalog_marketplace_offers` WHERE `offer_id` = @offerId LIMIT 1", new { offerId });
    }

    private sealed record MarketTotals(int AveragePrice, int Sold);
}
