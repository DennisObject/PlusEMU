using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

public interface ICatalogSnapshotService
{
    CatalogOfferSnapshot CaptureOffer(CatalogOffer offer);
    CatalogPageSnapshot CapturePage(CatalogPage page, int preselectOfferId);
    CatalogIndexSnapshot CaptureIndex(Habbo habbo, ICollection<CatalogPage> pages);
    ClubGiftsSnapshot CaptureClubGifts(ClubGiftInfo info);
}

public sealed class CatalogSnapshotService(ICatalogManager catalog, TimeProvider time) : ICatalogSnapshotService
{
    // The client rejects deeper trees, so pages below this depth are left out.
    private const int MaximumIndexDepth = 20;
    private const string UnknownBotFigure = "hd-180-7.ea-1406-62.ch-210-1321.hr-831-49.ca-1813-62.sh-295-1321.lg-285-92";

    // A single offer is named by what it sells, as the furni's info stand knows it.
    public CatalogOfferSnapshot CaptureOffer(CatalogOffer offer) =>
        CaptureOffer(offer, offer.Product.Type == CatalogProductType.Furni ? offer.Definition!.ItemName : offer.LocalizationKey);

    public CatalogPageSnapshot CapturePage(CatalogPage page, int preselectOfferId)
    {
        var now = time.GetUtcNow();
        var offers = page.Layout is "frontpage" or "club_buy" or "vip_buy" or "loyalty_vip_buy"
            ? []
            : page.Offers.Values.Select(offer => CaptureOffer(offer, offer.LocalizationKey)).ToImmutableArray();
        var promotions = catalog.Promotions
            .Where(promotion => !promotion.HasExpiredAt(now))
            .OrderBy(promotion => promotion.Position)
            .Select(promotion => new CatalogPromotionSnapshot(promotion.Position, promotion.Title, promotion.Image, promotion.ItemType,
                promotion.OfferId, promotion.ProductCode, promotion.PageLink, (int)Math.Clamp(promotion.RemainingAt(now).Ticks / TimeSpan.TicksPerSecond, 0, int.MaxValue)))
            .ToImmutableArray();

        return new CatalogPageSnapshot(page.Id, CatalogModes.Normal, page.Layout, page.Images.ToImmutableArray(), page.Texts.ToImmutableArray(),
            offers, preselectOfferId, promotions);
    }

    public CatalogIndexSnapshot CaptureIndex(Habbo habbo, ICollection<CatalogPage> pages)
    {
        var children = pages.Where(page => page.IsAvailableTo(habbo)).ToLookup(page => page.ParentId);

        return new CatalogIndexSnapshot(CatalogModes.Normal, children[-1].Select(page => IndexNode(children, page, 1)).ToImmutableArray());
    }

    public ClubGiftsSnapshot CaptureClubGifts(ClubGiftInfo info) => new(
        info.DaysUntilNextGift,
        info.Available,
        info.Gifts.Select(gift => CaptureClubGiftOffer(gift.Offer)).ToImmutableArray(),
        info.Gifts.Select(gift => new ClubGiftEntry(gift.Offer.Id, gift.DaysRequired,
            info.Available > 0 && info.PastDays >= gift.DaysRequired)).ToImmutableArray());

    // Gifts are free, regardless of the ordinary catalog price of the same chair.
    private CatalogOfferSnapshot CaptureClubGiftOffer(CatalogOffer offer) => CaptureOffer(new CatalogOffer
    {
        Id = offer.Id,
        LocalizationKey = offer.LocalizationKey,
        ClubLevel = offer.ClubLevel,
        PreviewImage = offer.PreviewImage,
        BulkPurchase = false,
        Products = offer.Products.Select(product => product with { ExtraParam = "" }).ToList()
    }, offer.LocalizationKey);

    private CatalogOfferSnapshot CaptureOffer(CatalogOffer offer, string localizationId)
    {
        return new CatalogOfferSnapshot(
            offer.Id,
            localizationId,
            offer.CostCredits,
            offer.CostPoints,
            offer.PointsType,
            ItemUtility.CanGiftItem(offer),
            offer.Products.Select(product => CaptureProduct(offer, product)).ToImmutableArray(),
            offer.ClubLevel,
            ItemUtility.CanSelectAmount(offer),
            offer.PreviewImage,
            offer.Enabled);
    }

    private CatalogProductSnapshot CaptureProduct(CatalogOffer offer, CatalogProduct product)
    {
        var limited = offer.IsLimited && ReferenceEquals(product, offer.Product);

        return new CatalogProductSnapshot(
            product.WireType,
            product.ClassId,
            product.Type switch
            {
                CatalogProductType.Badge => product.BadgeCode,
                CatalogProductType.Habbicon => product.HabbiconId.ToString(CultureInfo.InvariantCulture),
                CatalogProductType.Bot => catalog.TryGetBot((uint)product.BotPresetId, out var bot) ? bot.Figure ?? UnknownBotFigure : UnknownBotFigure,
                _ => product.ExtraParam
            },
            product.Amount,
            limited,
            limited ? offer.LimitedStack : 0,
            limited ? offer.LimitedStack - Math.Min(offer.LimitedSells, offer.LimitedStack) : 0);
    }

    private static CatalogIndexNode IndexNode(ILookup<int, CatalogPage> children, CatalogPage page, int depth)
    {
        var childPages = depth < MaximumIndexDepth ? children[page.Id].ToImmutableArray() : [];

        return new CatalogIndexNode(
            page.Visible,
            page.Icon,
            // A disabled page stays in the tree as a heading the client cannot open.
            page.Enabled ? page.Id : -1,
            page.ParentId,
            page.Link,
            page.Caption,
            page.Enabled ? page.Offers.Keys.ToImmutableArray() : [],
            childPages.Select(child => IndexNode(children, child, depth + 1)).ToImmutableArray());
    }
}
