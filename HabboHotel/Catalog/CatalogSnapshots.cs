using System.Collections.Immutable;

namespace Plus.HabboHotel.Catalog;

// Immutable, already-resolved catalog data. Packet composers only write these values.
public sealed record CatalogOfferSnapshot(
    int WireOfferId,
    string LocalizationId,
    int CostCredits,
    int Price,
    int PriceType,
    bool CanGift,
    CatalogOfferProducts Products,
    int ClubLevel,
    bool CanSelectAmount,
    string PreviewImage,
    bool OfferEnabled);

public abstract record CatalogOfferProducts;

public sealed record HabbiconProducts(int HabbiconId) : CatalogOfferProducts;

public sealed record DealProducts(ImmutableArray<DealProduct> Items) : CatalogOfferProducts;

public sealed record DealProduct(string ProductType, string ItemName, int SpriteId, int Amount);

public sealed record ItemProducts(
    string Badge,
    string ProductType,
    string ItemName,
    int SpriteId,
    bool HasExtra,
    string? Extra,
    int Amount,
    bool IsLimited,
    uint LimitedStack,
    uint LimitedRemaining) : CatalogOfferProducts;

public sealed record CatalogPageSnapshot(
    int Id,
    string Mode,
    string Layout,
    ImmutableArray<string> Strings1,
    ImmutableArray<string> Strings2,
    ImmutableArray<CatalogOfferSnapshot> Offers,
    int PreselectOfferId,
    ImmutableArray<CatalogPromotionSnapshot> Promotions);

public sealed record CatalogPromotionSnapshot(
    int Position,
    string? Title,
    string? Image,
    int ItemType,
    int OfferId,
    string? ProductCode,
    string? PageLink,
    int SecondsLeft);

public sealed record CatalogIndexSnapshot(string Mode, ImmutableArray<CatalogIndexNode> Roots);

public sealed record CatalogIndexNode(
    bool Visible,
    int Icon,
    int WireId,
    int ParentId,
    string? Link,
    string? Caption,
    ImmutableArray<int> OfferIds,
    ImmutableArray<CatalogIndexNode> Children);

public sealed record ClubGiftsSnapshot(int DaysUntilNextGift, int Available, ImmutableArray<CatalogOfferSnapshot> Offers, ImmutableArray<ClubGiftEntry> Gifts);

public sealed record ClubGiftEntry(int WireOfferId, int DaysRequired, bool Unlocked);
