using System.Collections.Immutable;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

public interface ICatalogSnapshotService
{
    CatalogOfferSnapshot CaptureOffer(CatalogItem item);
    CatalogPageSnapshot CapturePage(CatalogPage page, int preselectOfferId);
    CatalogIndexSnapshot CaptureIndex(Habbo habbo, ICollection<CatalogPage> pages);
    ClubGiftsSnapshot CaptureClubGifts(ClubGiftInfo info);
}

public sealed class CatalogSnapshotService(ICatalogManager catalog, TimeProvider time) : ICatalogSnapshotService
{
    // The client rejects deeper trees, so pages below this depth are left out.
    private const int MaximumIndexDepth = 20;
    private const string UnknownBotFigure = "hd-180-7.ea-1406-62.ch-210-1321.hr-831-49.ca-1813-62.sh-295-1321.lg-285-92";

    public CatalogOfferSnapshot CaptureOffer(CatalogItem item) =>
        CaptureOffer(item, item.WireOfferId, item.HabbiconId > 0 ? item.CatalogName : item.Definition.ItemName);

    public CatalogPageSnapshot CapturePage(CatalogPage page, int preselectOfferId)
    {
        var now = time.GetUtcNow();
        var offers = page.Layout is "frontpage" or "club_buy" or "vip_buy" or "loyalty_vip_buy"
            ? []
            : page.Offers.Values.Select(item => CaptureOffer(item, item.WireOfferId, item.CatalogName)).ToImmutableArray();
        var promotions = catalog.Promotions
            .Where(promotion => !promotion.HasExpiredAt(now))
            .OrderBy(promotion => promotion.Position)
            .Select(promotion => new CatalogPromotionSnapshot(promotion.Position, promotion.Title, promotion.Image, promotion.ItemType,
                promotion.OfferId, promotion.ProductCode, promotion.PageLink, (int)Math.Clamp(promotion.RemainingAt(now).Ticks / TimeSpan.TicksPerSecond, 0, int.MaxValue)))
            .ToImmutableArray();

        return new CatalogPageSnapshot(page.Id, CatalogModes.Normal, page.Layout, page.PageStringsList1.ToImmutableArray(), page.PageStringsList2.ToImmutableArray(),
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
        info.Gifts.Select(gift => CaptureClubGiftOffer(gift.Item)).ToImmutableArray(),
        info.Gifts.Select(gift => new ClubGiftEntry(gift.Item.WireOfferId, gift.DaysRequired,
            info.Available > 0 && info.PastDays >= gift.DaysRequired)).ToImmutableArray());

    private CatalogOfferSnapshot CaptureClubGiftOffer(CatalogItem item) =>
        // Gifts are free, regardless of the ordinary catalog price of the same chair.
        CaptureOffer(new CatalogItem
        {
            Definition = item.Definition,
            Amount = item.Amount,
            CatalogName = item.CatalogName,
            ExtraData = "",
            ClubLevel = item.ClubLevel,
            PreviewImage = item.PreviewImage,
        }, item.WireOfferId, item.CatalogName);

    private CatalogOfferSnapshot CaptureOffer(CatalogItem item, int wireOfferId, string localizationId)
    {
        var diamonds = item.CostDiamonds > 0;

        return new CatalogOfferSnapshot(
            wireOfferId,
            localizationId,
            item.CostCredits,
            diamonds ? item.CostDiamonds : item.CostPixels,
            diamonds ? 5 : 0,
            ItemUtility.CanGiftItem(item),
            CaptureProducts(item),
            item.ClubLevel,
            ItemUtility.CanSelectAmount(item),
            item.PreviewImage ?? string.Empty,
            item.HabbiconId > 0 ? item.HaveOffer : true);
    }

    private CatalogOfferProducts CaptureProducts(CatalogItem item)
    {
        if (item.HabbiconId > 0) {
            return new HabbiconProducts(item.HabbiconId);
        }

        if (item.Definition.InteractionType is InteractionType.Deal or InteractionType.Roomdeal) {
            return CaptureDeal(item.Definition.BehaviourData);
        }

        return CaptureItemProduct(item);
    }

    private DealProducts CaptureDeal(int dealId) => new(
        catalog.TryGetDeal(dealId, out var deal)
            ? deal.ItemDataList.Select(dealItem => new DealProduct(dealItem.Definition.ProductType, dealItem.Definition.ItemName,
                dealItem.Definition.SpriteId, dealItem.Amount)).ToImmutableArray()
            : []);

    private ItemProducts CaptureItemProduct(CatalogItem item)
    {
        var extra = CaptureExtra(item);

        return new ItemProducts(
            item.Badge,
            item.Definition.ProductType,
            item.Definition.ItemName,
            item.Definition.SpriteId,
            extra.HasExtra,
            extra.Value,
            item.Amount,
            item.IsLimited,
            item.LimitedEditionStack,
            item.IsLimited ? item.LimitedEditionStack - item.LimitedEditionSells : 0);
    }

    private (bool HasExtra, string? Value) CaptureExtra(CatalogItem item)
    {
        var interaction = item.Definition.InteractionType;

        if (interaction is InteractionType.Wallpaper or InteractionType.Floor or InteractionType.Landscape) {
            return (true, item.CatalogName.Split('_')[2]);
        }

        if (interaction == InteractionType.Bot) {
            return (true, catalog.TryGetBot(item.ItemId, out var bot) ? bot.Figure : UnknownBotFigure);
        }

        return (item.ExtraData != null, item.ExtraData);
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
            page.Enabled ? CatalogOfferIndex.OfficialOfferIds(page).ToImmutableArray() : [],
            childPages.Select(child => IndexNode(children, child, depth + 1)).ToImmutableArray());
    }
}
