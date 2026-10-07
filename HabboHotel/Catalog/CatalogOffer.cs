using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

public enum CatalogProductType
{
    Furni,
    Effect,
    Badge,
    Bot,
    Pet,
    Habbicon
}

// One thing an offer sells (catalog_offer_products).
public sealed record CatalogProduct
{
    public CatalogProductType Type { get; init; }
    public ItemDefinition? Definition { get; init; }
    public int EffectId { get; init; }
    public string BadgeCode { get; init; } = string.Empty;
    public int BotPresetId { get; init; }
    public int PetType { get; init; }
    public int HabbiconId { get; init; }
    public int Amount { get; init; } = 1;
    public string ExtraParam { get; init; } = string.Empty;

    // The product type the client knows: s, i, e, b, r, p or habbicon.
    public string WireType => Type switch
    {
        CatalogProductType.Furni => Definition!.ProductType,
        CatalogProductType.Effect => "e",
        CatalogProductType.Badge => "b",
        CatalogProductType.Bot => "r",
        CatalogProductType.Pet => "p",
        _ => "habbicon"
    };

    public int ClassId => Type switch
    {
        CatalogProductType.Furni => Definition!.SpriteId,
        CatalogProductType.Effect => EffectId,
        CatalogProductType.Pet => PetType,
        CatalogProductType.Habbicon => HabbiconId,
        _ => 0
    };
}

// An offer (catalog_offers). Its id is the id the client buys it with and furnidata names it by.
public sealed class CatalogOffer
{
    public int Id { get; set; }
    public string LocalizationKey { get; set; } = string.Empty;
    public int CostCredits { get; set; }
    public int CostPixels { get; set; }
    public int CostDiamonds { get; set; }
    public int ClubLevel { get; set; }
    public bool BulkPurchase { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string PreviewImage { get; set; } = string.Empty;
    public uint LimitedStack { get; set; }

    // Serials sold when the catalog was loaded or last sold here; purchases count in the database.
    public uint LimitedSells { get; set; }
    public IReadOnlyList<CatalogProduct> Products { get; set; } = [];

    public bool IsLimited => LimitedStack > 0;

    // What the offer sells besides badges given with it; a badge-only offer sells its badge.
    public CatalogProduct Product => Products.FirstOrDefault(product => product.Type != CatalogProductType.Badge) ?? Products[0];

    public ItemDefinition? Definition => Product.Definition;

    public int Amount => Product.Amount;

    public bool IsBundle => Products.Count(product => product.Type != CatalogProductType.Badge) > 1;

    // Badges given with the offer's product.
    public IEnumerable<string> AttachedBadges => Product.Type == CatalogProductType.Badge
        ? []
        : Products.Where(product => product.Type == CatalogProductType.Badge).Select(product => product.BadgeCode);

    public bool CanPurchase(Habbo habbo) => ClubLevel is >= 0 and <= 2 && ClubAccess.LevelFor(habbo.Access) >= ClubLevel;
}
