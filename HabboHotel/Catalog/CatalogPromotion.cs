namespace Plus.HabboHotel.Catalog;

public class CatalogPromotion
{
    // Front page item types the client understands.
    public const int CataloguePageItem = 0;
    public const int ProductOfferItem = 1;
    public const int ProductCodeItem = 2;

    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Image { get; set; }
    public string? PageLink { get; set; }
    public int Position { get; set; }
    public int ItemType { get; set; }
    public int OfferId { get; set; }
    public string? ProductCode { get; set; }
    private DateTimeOffset? _expiresAt;
    public DateTimeOffset? ExpiresAt
    {
        get => _expiresAt;
        set => _expiresAt = value?.ToUniversalTime();
    }

    public bool HasExpiredAt(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;

    public TimeSpan RemainingAt(DateTimeOffset now) =>
        ExpiresAt is { } expiry && expiry > now ? expiry - now : TimeSpan.Zero;
}
