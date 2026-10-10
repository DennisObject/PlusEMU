using Plus.Core.Settings;

namespace Plus.HabboHotel.Catalog.Marketplace;

public interface IMarketplaceFeePolicy
{
    /// <summary>Percentage of a listing the hotel keeps (the client's sellingFeePercentage).</summary>
    int SellingFeePercentage { get; }

    /// <summary>Revenue limit the client is told about; the client keeps it and does not use it in its price.</summary>
    int RevenueLimit { get; }

    /// <summary>Price at which the fee gains one half of its price-proportional term; always positive, because the client divides by it.</summary>
    int HalfTaxLimit { get; }

    /// <summary>What the hotel keeps of a buyer-facing price: the client's own formula, so the seller sees the same payout the server pays.</summary>
    int Fee(int price);
}

// The official marketplace window shows the seller `price - ceil(round(1000 * price * (sellingFeePercentage / 100 + 0.5 * price / halfTaxLimit)) / 1000)` for the price typed
// (MarketplaceView.calculateFinalPrice, WIN63-202609161723); the same numbers are sent in the marketplace configuration and used here, so both sides agree.
// Settings (all optional): catalog.marketplace.fee.percentage (1), catalog.marketplace.fee.half_tax_limit (int.MaxValue, i.e. practically flat), catalog.marketplace.fee.revenue_limit (0).
public sealed class MarketplaceFeePolicy(ISettingsManager settings) : IMarketplaceFeePolicy
{
    public int SellingFeePercentage => Math.Clamp(Read("catalog.marketplace.fee.percentage", 1), 0, 100);

    public int RevenueLimit => Math.Max(0, Read("catalog.marketplace.fee.revenue_limit", 0));

    public int HalfTaxLimit => Math.Max(1, Read("catalog.marketplace.fee.half_tax_limit", int.MaxValue));

    public int Fee(int price) => Fee(price, SellingFeePercentage, HalfTaxLimit);

    // A small halfTaxLimit makes the double exceed the int range at the top prices; the fee then saturates and the listing is refused as costing more than its price.
    internal static int Fee(int price, int sellingFeePercentage, int halfTaxLimit) =>
        (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Floor(1000d * price * (sellingFeePercentage / 100d + 0.5d * price / halfTaxLimit) + 0.5d) / 1000d));

    private int Read(string key, int fallback) => int.TryParse(settings.TryGetValue(key, fallback.ToString()), out var value) ? value : fallback;
}
