using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public class CatalogSnapshotTests
{
    // SHA-256 of the catalog payloads for every case in GoldenPayloads.
    private const string BaselineSha256 = "7d54ba455f501b89758e2c169463c9338b7b3570cfdc7b7d23b137f3cd9c8c16";

    [Fact]
    public void ComposedCatalogPayloadsMatchPreMigrationBaseline()
    {
        var lines = GoldenPayloads();

        Assert.Equal(16, lines.Count);
        Assert.Equal(BaselineSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n"))))));
    }

    [Fact]
    public void AttachedBadgeOfferIsAdvertisedAsUngiftableWhileOrdinaryFurnitureRemainsGiftable()
    {
        var item = new CatalogItem
        {
            Id = 30,
            OfferId = 30,
            CatalogName = "chair",
            Amount = 1,
            Definition = Def(InteractionType.None, "s", gift: true, type: ItemType.Floor)
        };
        Assert.True(Snapshots().CaptureOffer(item).CanGift);
        item.Badge = "ACH_Bonus";
        Assert.False(Snapshots().CaptureOffer(item).CanGift);
    }

    [Fact]
    public void RecomposedOfferIgnoresItemMutationAfterCapture()
    {
        var definition = Def(InteractionType.None, "s", name: "chair");
        var offer = Offer(30, "chair_x", Badge("ADM"), Furni(definition));
        offer.CostCredits = 1;
        var snapshot = Snapshots().CaptureOffer(offer);
        var before = Writes(new CatalogOfferComposer(snapshot));

        offer.Products = [Badge("CHANGED"), Furni(definition)];
        offer.CostCredits = 99;
        definition.ItemName = "OTHER";

        Assert.Equal(before, Writes(new CatalogOfferComposer(snapshot)));
        Assert.NotEqual(before, Writes(new CatalogOfferComposer(Snapshots().CaptureOffer(offer))));
    }

    [Fact]
    public void PromotionExpiryAndCountdownUseOneCapturedClock()
    {
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1_000));
        var catalog = Catalog(
            new CatalogPromotion { Position = 1, Title = "Live", Image = "live", ItemType = CatalogPromotion.CataloguePageItem, PageLink = "page1", ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1_500) },
            new CatalogPromotion { Position = 2, Title = "Gone", Image = "gone", ItemType = CatalogPromotion.CataloguePageItem, PageLink = "page2", ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1_000) });
        var page = Page(5, "default_3x3");

        var snapshot = new CatalogSnapshotService(catalog, clock).CapturePage(page, -1);

        var promotion = Assert.Single(snapshot.Promotions);
        Assert.Equal("Live", promotion.Title);
        Assert.Equal(500, promotion.SecondsLeft);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public void UnknownBotFallsBackToTheDefaultFigure()
    {
        var offer = Offer(41, "bot_y", new CatalogProduct { Type = CatalogProductType.Bot, BotPresetId = 556 });
        offer.CostPixels = 9;

        var writes = Writes(new CatalogOfferComposer(Snapshots().CaptureOffer(offer)));

        Assert.Contains("hd-180-7.ea-1406-62.ch-210-1321.hr-831-49.ca-1813-62.sh-295-1321.lg-285-92", writes);
    }

    [Fact]
    public void LimitedOffersReportTheirRemainingStock()
    {
        var offer = Offer(60, "ltd_x", Furni(Def(InteractionType.None, "i", sprite: 8)));
        offer.LimitedStack = 10;
        offer.LimitedSells = 3;

        var product = Assert.Single(Snapshots().CaptureOffer(offer).Products);

        Assert.True(product.IsLimited);
        Assert.Equal(10u, product.LimitedStack);
        Assert.Equal(7u, product.LimitedRemaining);
    }

    [Fact]
    public void CapturedPageDoesNotFollowLaterSourceMutation()
    {
        var page = Page(5, "default_3x3");
        page.Images = ["a"];
        page.Offers[30] = Offer(30, "badge_x", Badge("ADM"));
        var snapshot = Snapshots().CapturePage(page, -1);
        var before = Writes(new CatalogPageComposer(snapshot));

        page.Images.Add("z");
        page.Offers.Clear();

        Assert.Equal(before, Writes(new CatalogPageComposer(snapshot)));
        Assert.Single(snapshot.Strings1);
        Assert.Single(snapshot.Offers);
    }

    [Fact]
    public void CapturedBundleAndClubGiftsDoNotFollowLaterSourceMutation()
    {
        var bundle = Offer(20, "deal_a", Furni(Def(InteractionType.None, "s", sprite: 4)), Furni(Def(InteractionType.None, "i", sprite: 5), amount: 2));
        var offer = Snapshots().CaptureOffer(bundle);
        var bundleBefore = Writes(new CatalogOfferComposer(offer));

        bundle.Products = [.. bundle.Products, Furni(Def(InteractionType.None, "i"))];

        Assert.Equal(bundleBefore, Writes(new CatalogOfferComposer(offer)));
        Assert.Equal(2, offer.Products.Length);
        Assert.False(offer.CanGift);
        Assert.False(offer.CanSelectAmount);

        var gift = Offer(700, "club_a", Furni(Def(InteractionType.None, "i", sprite: 11, gift: true, type: ItemType.Floor)));
        gift.PreviewImage = "p.png";
        var gifts = new List<ClubGift> { new(gift, 1) };
        var club = Snapshots().CaptureClubGifts(new ClubGiftInfo(3, 1, 5, gifts));
        var clubBefore = Writes(new ClubGiftsComposer(club));

        gifts.Add(new ClubGift(gift, 9));

        Assert.Equal(clubBefore, Writes(new ClubGiftsComposer(club)));
        Assert.Single(club.Gifts);
    }

    private static List<string> GoldenPayloads()
    {
        var snapshots = Snapshots(Catalog(Promotions().ToArray()));
        var lines = new List<string>();
        void Add(string name, IServerPacket composer) => lines.Add(name + ": " + Writes(composer));

        var hab = Offer(12, "toast_toast", new CatalogProduct { Type = CatalogProductType.Habbicon, HabbiconId = 61 });
        hab.CostCredits = 5;
        Add("habbicon", new CatalogOfferComposer(snapshots.CaptureOffer(hab)));
        var disabled = Offer(13, "t2", new CatalogProduct { Type = CatalogProductType.Habbicon, HabbiconId = 62 });
        disabled.CostCredits = 5;
        disabled.Enabled = false;
        Add("habbicon-nooffer", new CatalogOfferComposer(snapshots.CaptureOffer(disabled)));
        var deal = Offer(20, "deal_a", Badge("ADM"), Furni(Def(InteractionType.None, "i", sprite: 5), amount: 2));
        deal.CostDiamonds = 3;
        Add("deal", new CatalogOfferComposer(snapshots.CaptureOffer(deal)));
        var badge = BadgeOffer();
        badge.CostPixels = 2;
        Add("badge", new CatalogOfferComposer(snapshots.CaptureOffer(badge)));
        Add("bot", new CatalogOfferComposer(snapshots.CaptureOffer(BotOffer(40, "bot_x", 555))));
        Add("bot-unknown", new CatalogOfferComposer(snapshots.CaptureOffer(BotOffer(41, "bot_y", 556))));
        Add("wallpaper", new CatalogOfferComposer(snapshots.CaptureOffer(WallpaperOffer())));
        Add("ltd", new CatalogOfferComposer(snapshots.CaptureOffer(LimitedOffer())));
        var plain = Offer(61, "plain", Furni(Def(InteractionType.None, "i", sprite: 8, gift: true, type: ItemType.Floor), amount: 2));
        plain.CostCredits = 4;
        Add("plain-extra-empty", new CatalogOfferComposer(snapshots.CaptureOffer(plain)));
        var gift = Offer(62, "gift", Furni(Def(InteractionType.None, "i", sprite: 9, gift: true, type: ItemType.Floor), extra: "data"));
        gift.CostPixels = 4;
        gift.ClubLevel = 2;
        gift.PreviewImage = "catalogue/x.png";
        Add("plain-gift", new CatalogOfferComposer(snapshots.CaptureOffer(gift)));

        var page = Page(5, "default_3x3");
        page.Images = ["a", "b"];
        page.Texts = ["c"];

        foreach (var offer in new[] { hab, BadgeOffer(), WallpaperOffer(), LimitedOffer(), BotOffer(40, "bot_x", 555) }) {
            page.Offers[offer.Id] = offer;
        }

        Add("page", new CatalogPageComposer(snapshots.CapturePage(page, 30)));
        Add("page-preselect-none", new CatalogPageComposer(snapshots.CapturePage(page, -1)));
        var front = Page(6, "frontpage");
        front.Offers[hab.Id] = hab;
        Add("page-frontpage", new CatalogPageComposer(snapshots.CapturePage(front, -1)));

        var client = HabbiconTestSupport.Client(EditorTestSupport.Player()).Client;
        var pages = new List<CatalogPage> { Tree(1, -1), Tree(2, 1, offerIds: 7), Tree(3, 2), Tree(4, 3, enabled: false) };
        Add("index", new CatalogIndexComposer(snapshots.CaptureIndex(client.GetHabbo(), pages)));

        var gift1 = Offer(700, "club_a", Furni(Def(InteractionType.None, "i", sprite: 11, gift: true, type: ItemType.Floor)));
        gift1.ClubLevel = 2;
        gift1.PreviewImage = "p.png";
        gift1.CostCredits = 99;
        gift1.LimitedStack = 5;
        var gift2 = Offer(701, "club_b", Badge("CLUB"));
        gift2.ClubLevel = 1;
        gift2.PreviewImage = "q.png";
        Add("club-gifts", new ClubGiftsComposer(snapshots.CaptureClubGifts(new ClubGiftInfo(3, 2, 5, [new ClubGift(gift1, 1), new ClubGift(gift2, 9)]))));
        Add("club-gifts-none", new ClubGiftsComposer(snapshots.CaptureClubGifts(new ClubGiftInfo(3, 0, 0, [new ClubGift(gift1, 9)]))));

        return lines;
    }

    private static CatalogOffer BadgeOffer()
    {
        var offer = Offer(30, "badge_x", Badge("ADM"), Badge("ADM"));
        offer.CostCredits = 1;

        return offer;
    }

    private static CatalogOffer BotOffer(int id, string name, int preset)
    {
        var offer = Offer(id, name, new CatalogProduct { Type = CatalogProductType.Bot, BotPresetId = preset });
        offer.CostPixels = 9;

        return offer;
    }

    private static CatalogOffer WallpaperOffer()
    {
        var offer = Offer(50, "wallpaper_x_abc_def", Furni(Def(InteractionType.Wallpaper, "i", sprite: 7), extra: "abc"));
        offer.CostCredits = 4;

        return offer;
    }

    private static CatalogOffer LimitedOffer()
    {
        var offer = Offer(60, "ltd_x", Furni(Def(InteractionType.None, "i", sprite: 8), extra: "ex"));
        offer.CostCredits = 4;
        offer.LimitedStack = 10;
        offer.LimitedSells = 3;

        return offer;
    }

    private static CatalogOffer Offer(int id, string localizationKey, params CatalogProduct[] products) =>
        new() { Id = id, LocalizationKey = localizationKey, Products = products };

    private static CatalogProduct Furni(ItemDefinition definition, int amount = 1, string extra = "") =>
        new() { Type = CatalogProductType.Furni, Definition = definition, Amount = amount, ExtraParam = extra };

    private static CatalogProduct Badge(string code) => new() { Type = CatalogProductType.Badge, BadgeCode = code };

    private static IEnumerable<CatalogPromotion> Promotions() =>
    [
        new() { Position = 2, Title = "T2", Image = "i2", ItemType = CatalogPromotion.ProductOfferItem, OfferId = 12 },
        new() { Position = 1, Title = "T1", Image = "i1", ItemType = CatalogPromotion.ProductCodeItem, ProductCode = "P" },
        new() { Position = 3, Title = "T3", Image = "i3", ItemType = CatalogPromotion.CataloguePageItem, PageLink = "page5", ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1) },
        new() { Position = 4, Title = "T4", Image = "i4", ItemType = CatalogPromotion.CataloguePageItem, PageLink = "page6" },
    ];

    private static CatalogSnapshotService Snapshots(ICatalogManager? catalog = null) =>
        new(catalog ?? Catalog(), TimeProvider.System);

    private static ICatalogManager Catalog(params CatalogPromotion[] promotions) =>
        Proxy<ICatalogManager>((method, args) => method switch
        {
            "TryGetBot" => Out(args, 1, (uint)args[0]! == 555 ? new CatalogBot { Id = 555, Figure = "hd-1.ch-2" } : null),
            "get_Promotions" => promotions,
            _ => throw new InvalidOperationException(method),
        });

    private static ItemDefinition Def(InteractionType interaction, string productType, int behaviour = 0, string name = "item", int sprite = 1, bool gift = false, ItemType type = ItemType.Floor) => new()
    {
        InteractionType = interaction,
        ProductType = productType,
        ItemName = name,
        SpriteId = sprite,
        BehaviourData = behaviour,
        AllowGift = gift,
        Type = type,
    };

    private static CatalogPage Page(int id, string layout) => new()
    {
        Id = id,
        ParentId = -1,
        Enabled = true,
        Visible = true,
        Icon = id,
        Link = "page" + id,
        Caption = "Page " + id,
        Layout = layout,
    };

    private static CatalogPage Tree(int id, int parent, bool enabled = true, params int[] offerIds)
    {
        var page = new CatalogPage { Id = id, ParentId = parent, Enabled = enabled, Visible = true, Icon = id, Link = "page" + id, Caption = "Page " + id, Layout = "default_3x3" };

        foreach (var offerId in offerIds) {
            page.Offers[offerId] = Offer(offerId, "offer" + offerId, Furni(Def(InteractionType.None, "i")));
        }

        return page;
    }

    private static string Writes(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return string.Join("|", packet.Writes.Select(write => $"{write.GetType().Name}:{write}"));
    }

    private static bool Out<T>(object?[] args, int index, T? value) where T : class
    {
        args[index] = value;

        return value != null;
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class => CatalogSnapshotTestSupport.Proxy<T>(call);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return now;
        }
    }
}

// Wire tests that never reach the catalog lookups share this service with a catalog that fails if touched.
internal static class CatalogSnapshotTestSupport
{
    public static CatalogSnapshotService Snapshots() => new(Proxy<ICatalogManager>((method, _) => throw new InvalidOperationException(method)), TimeProvider.System);

    public static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;

        return proxy;
    }

    public class TestProxy : System.Reflection.DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
}
