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
    public int Unknown { get; set; }
    public string? PageLink { get; set; }
    public int ParentId { get; set; }
    public int Position { get; set; }
    public int ItemType { get; set; }
    public int OfferId { get; set; }
    public string? ProductCode { get; set; }
    public int ExpiresAt { get; set; }

    public bool HasExpired(long now) => ExpiresAt > 0 && ExpiresAt <= now;

    public int SecondsLeft(long now) => ExpiresAt > 0 ? (int)Math.Max(0, ExpiresAt - now) : 0;
}
