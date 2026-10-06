using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Marketplace;

public interface IMarketplaceManager
{
    Dictionary<int, int> MarketAverages { get; }
    Dictionary<int, int> MarketCounts { get; }
    List<int> MarketItemKeys { get; }
    List<MarketOffer> MarketItems { get; }
    int AvgPriceForSprite(int spriteId);
    string FormatTimestampString();
    double FormatTimestamp();
    int OfferCountForSprite(uint spriteId);
    MarketplaceItemStats ItemStats(uint spriteId);
    MarketplaceOwnOffers OwnOffers(int userId);
    int CalculateComissionPrice(float price);

    Task<bool> TryCancelOffer(Habbo habbo, uint offerId);
    Task<MarketOffer?> GetOffer(uint offerId);
    Task DeleteOffer(uint offerId);
}

public sealed record MarketplaceItemStats(int AveragePrice, int OfferCount);
public sealed record MarketplaceOwnOffers(int AccumulatedAmount, IReadOnlyList<MarketplaceOwnOffer> Offers);
public sealed record MarketplaceOwnOffer(int OfferId, int State, int SpriteId, int LimitedNumber, int LimitedStack, int TotalPrice, int MinutesRemaining);
