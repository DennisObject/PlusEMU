using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Catalog;

public class CatalogItem
{
    public int Id { get; set; }
    public int HabbiconId { get; set; }
    public uint ItemId { get; set; }
    public ItemDefinition Definition { get; set; }
    public int Amount { get; set; }
    public int CostCredits { get; set; }
    public string ExtraData { get; set; }
    public bool HaveOffer { get; set; }
    public bool IsLimited { get; set; }
    public string CatalogName { get; set; }
    public int PageId { get; set; }
    public int CostPixels { get; set; }
    public uint LimitedEditionStack { get; set; }
    public uint LimitedEditionSells { get; set; }
    public int CostDiamonds { get; set; }
    public string Badge { get; set; }
    public int OfferId { get; set; }
    public int ClubLevel { get; set; }
    public string PreviewImage { get; set; } = string.Empty;
    public int OrderNum { get; set; }

    private int? _wireOfferId;

    // The id the client sees and buys with on this item's page; CatalogOfferIndex assigns it.
    public int WireOfferId
    {
        get => _wireOfferId ?? (OfferId > 0 ? OfferId : Id);
        set => _wireOfferId = value;
    }
}