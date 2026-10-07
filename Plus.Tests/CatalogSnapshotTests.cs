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
    // SHA-256 of the pre-migration catalog payloads for every case in GoldenPayloads.
    private const string BaselineSha256 = "40e4801d2c8e602ed6588d0b34c65a56eb3d6fdfd08be067795067a70543bb68";

    [Fact]
    public void ComposedCatalogPayloadsMatchPreMigrationBaseline()
    {
        var lines = GoldenPayloads();

        Assert.Equal(17, lines.Count);
        Assert.Equal(BaselineSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n"))))));
    }

    [Fact]
    public void AttachedBadgeOfferIsAdvertisedAsUngiftableWhileOrdinaryFurnitureRemainsGiftable()
    {
        var item = new CatalogItem { Id = 30, OfferId = 30, CatalogName = "chair", Amount = 1,
            Definition = Def(InteractionType.None, "s", gift: true, type: ItemType.Floor) };
        Assert.True(Snapshots().CaptureOffer(item).CanGift);
        item.Badge = "ACH_Bonus";
        Assert.False(Snapshots().CaptureOffer(item).CanGift);
    }

    [Fact]
    public void RecomposedOfferIgnoresItemMutationAfterCapture()
    {
        var item = new CatalogItem { Id = 30, OfferId = 30, CatalogName = "badge_x", Badge = "ADM", CostCredits = 1, Amount = 1, HaveOffer = true, Definition = Def(InteractionType.Badge, "b", name: "ADM") };
        var snapshot = Snapshots().CaptureOffer(item);
        var before = Writes(new CatalogOfferComposer(snapshot));

        item.Badge = "CHANGED";
        item.CostCredits = 99;
        item.Definition.ItemName = "OTHER";

        Assert.Equal(before, Writes(new CatalogOfferComposer(snapshot)));
        Assert.NotEqual(before, Writes(new CatalogOfferComposer(Snapshots().CaptureOffer(item))));
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
        var item = new CatalogItem { Id = 41, OfferId = 41, CatalogName = "bot_y", ItemId = 556, CostPixels = 9, Amount = 1, Definition = Def(InteractionType.Bot, "s", sprite: 3) };

        var writes = Writes(new CatalogOfferComposer(Snapshots().CaptureOffer(item)));

        Assert.Contains("hd-180-7.ea-1406-62.ch-210-1321.hr-831-49.ca-1813-62.sh-295-1321.lg-285-92", writes);
    }

    [Fact]
    public void CapturedPageDoesNotFollowLaterSourceMutation()
    {
        var page = Page(5, "default_3x3");
        page.PageStringsList1 = ["a"];
        page.Items[30] = new CatalogItem { Id = 30, OfferId = 30, CatalogName = "badge_x", Badge = "ADM", CostCredits = 1, Amount = 1, HaveOffer = true, Definition = Def(InteractionType.Badge, "b", name: "ADM") };
        new CatalogOfferIndex().Build([page]);
        var snapshot = Snapshots().CapturePage(page, -1);
        var before = Writes(new CatalogPageComposer(snapshot));

        page.PageStringsList1.Add("z");
        page.Offers.Clear();
        page.Items.Clear();

        Assert.Equal(before, Writes(new CatalogPageComposer(snapshot)));
        Assert.Single(snapshot.Strings1);
        Assert.Single(snapshot.Offers);
    }

    [Fact]
    public void CapturedDealAndClubGiftsDoNotFollowLaterSourceMutation()
    {
        var deal = new CatalogDeal { Id = 77, ItemDataList = [new CatalogItem { Definition = Def(InteractionType.Badge, "b", name: "ADM") }] };
        var catalog = Proxy<ICatalogManager>((method, args) => method == "TryGetDeal" ? Out(args, 1, deal) : throw new InvalidOperationException(method));
        var dealItem = new CatalogItem { Id = 20, OfferId = 20, CatalogName = "deal_a", CostCredits = 1, Definition = Def(InteractionType.Deal, "s", behaviour: 77) };
        var offer = Snapshots(catalog).CaptureOffer(dealItem);
        var dealBefore = Writes(new CatalogOfferComposer(offer));

        deal.ItemDataList.Add(new CatalogItem { Definition = Def(InteractionType.None, "i") });

        Assert.Equal(dealBefore, Writes(new CatalogOfferComposer(offer)));
        Assert.Single(((DealProducts)offer.Products).Items);

        var gift = new CatalogItem { Id = 70, CatalogName = "club_a", Amount = 1, PreviewImage = "p.png", Definition = Def(InteractionType.None, "i", sprite: 11, gift: true, type: ItemType.Floor) };
        gift.WireOfferId = 700;
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

        var hab = new CatalogItem { Id = 10, OfferId = 12, CatalogName = "toast_toast", HabbiconId = 61, CostCredits = 5, Amount = 1, HaveOffer = true, Definition = null! };
        Add("habbicon", new CatalogOfferComposer(snapshots.CaptureOffer(hab)));
        Add("habbicon-nooffer", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 11, OfferId = 13, CatalogName = "t2", HabbiconId = 62, CostCredits = 5, Amount = 1, HaveOffer = false, Definition = null! })));
        Add("deal", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 20, OfferId = 20, CatalogName = "deal_a", CostDiamonds = 3, Definition = Def(InteractionType.Deal, "s", behaviour: 77) })));
        Add("deal-missing", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 21, OfferId = 21, CatalogName = "deal_b", CostCredits = 2, Definition = Def(InteractionType.Roomdeal, "s", behaviour: 78) })));
        Add("badge", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 30, OfferId = 30, CatalogName = "badge_x", Badge = "ADM", CostCredits = 1, CostPixels = 2, Amount = 1, HaveOffer = true, Definition = Def(InteractionType.Badge, "b", name: "ADM") })));
        Add("bot", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 40, OfferId = 40, CatalogName = "bot_x", ItemId = 555, CostPixels = 9, Amount = 1, Definition = Def(InteractionType.Bot, "s", sprite: 3) })));
        Add("bot-unknown", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 41, OfferId = 41, CatalogName = "bot_y", ItemId = 556, CostPixels = 9, Amount = 1, Definition = Def(InteractionType.Bot, "s", sprite: 3) })));
        Add("wallpaper", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 50, OfferId = 50, CatalogName = "wallpaper_x_abc_def", CostCredits = 4, Amount = 1, HaveOffer = true, Definition = Def(InteractionType.Wallpaper, "i", sprite: 7) })));
        Add("ltd", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 60, OfferId = 60, CatalogName = "ltd_x", CostCredits = 4, Amount = 1, IsLimited = true, LimitedEditionStack = 10, LimitedEditionSells = 3, ExtraData = "ex", HaveOffer = true, Definition = Def(InteractionType.None, "i", sprite: 8) })));
        Add("plain-extra-null", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 61, OfferId = 61, CatalogName = "plain", CostCredits = 4, Amount = 2, ExtraData = null!, HaveOffer = true, Definition = Def(InteractionType.None, "i", sprite: 8, gift: true, type: ItemType.Floor) })));
        Add("plain-gift", new CatalogOfferComposer(snapshots.CaptureOffer(new CatalogItem { Id = 62, OfferId = 62, CatalogName = "gift", CostPixels = 4, Amount = 1, ExtraData = "data", ClubLevel = 2, PreviewImage = "catalogue/x.png", HaveOffer = true, Definition = Def(InteractionType.None, "i", sprite: 9, gift: true, type: ItemType.Floor) })));

        var page = Page(5, "default_3x3");
        page.PageStringsList1 = ["a", "b"];
        page.PageStringsList2 = ["c"];
        page.Items[10] = hab;
        page.Items[30] = new CatalogItem { Id = 30, OfferId = 30, CatalogName = "badge_x", Badge = "ADM", CostCredits = 1, Amount = 1, HaveOffer = true, Definition = Def(InteractionType.Badge, "b", name: "ADM") };
        page.Items[50] = new CatalogItem { Id = 50, OfferId = 50, CatalogName = "wallpaper_x_abc_def", CostCredits = 4, Amount = 1, HaveOffer = true, Definition = Def(InteractionType.Wallpaper, "i", sprite: 7) };
        page.Items[60] = new CatalogItem { Id = 60, OfferId = 60, CatalogName = "ltd_x", CostCredits = 4, Amount = 1, IsLimited = true, LimitedEditionStack = 10, LimitedEditionSells = 3, ExtraData = "ex", HaveOffer = true, Definition = Def(InteractionType.None, "i", sprite: 8) };
        page.Items[40] = new CatalogItem { Id = 40, OfferId = 40, CatalogName = "bot_x", ItemId = 555, CostPixels = 9, Amount = 1, Definition = Def(InteractionType.Bot, "s", sprite: 3) };
        new CatalogOfferIndex().Build([page]);
        Add("page", new CatalogPageComposer(snapshots.CapturePage(page, 30)));
        Add("page-preselect-none", new CatalogPageComposer(snapshots.CapturePage(page, -1)));
        var front = Page(6, "frontpage");
        front.Items[10] = hab;
        new CatalogOfferIndex().Build([front]);
        Add("page-frontpage", new CatalogPageComposer(snapshots.CapturePage(front, -1)));

        var client = HabbiconTestSupport.Client(EditorTestSupport.Player()).Client;
        var pages = new List<CatalogPage> { Tree(1, -1), Tree(2, 1, offerIds: 7), Tree(3, 2), Tree(4, 3, enabled: false) };
        Add("index", new CatalogIndexComposer(snapshots.CaptureIndex(client.GetHabbo(), pages)));

        var gift1 = new CatalogItem { Id = 70, OfferId = 0, CatalogName = "club_a", Amount = 1, ClubLevel = 2, PreviewImage = "p.png", CostCredits = 99, IsLimited = true, Definition = Def(InteractionType.None, "i", sprite: 11, gift: true, type: ItemType.Floor) };
        gift1.WireOfferId = 700;
        var gift2 = new CatalogItem { Id = 71, OfferId = 0, CatalogName = "club_b", Amount = 1, ClubLevel = 1, PreviewImage = "q.png", Definition = Def(InteractionType.Badge, "b", name: "CLUB") };
        gift2.WireOfferId = 701;
        Add("club-gifts", new ClubGiftsComposer(snapshots.CaptureClubGifts(new ClubGiftInfo(3, 2, 5, [new ClubGift(gift1, 1), new ClubGift(gift2, 9)]))));
        Add("club-gifts-none", new ClubGiftsComposer(snapshots.CaptureClubGifts(new ClubGiftInfo(3, 0, 0, [new ClubGift(gift1, 9)]))));

        return lines;
    }

    private static IEnumerable<CatalogPromotion> Promotions() =>
    [
        new() { Position = 2, Title = "T2", Image = "i2", ItemType = CatalogPromotion.ProductOfferItem, OfferId = 12 },
        new() { Position = 1, Title = "T1", Image = "i1", ItemType = CatalogPromotion.ProductCodeItem, ProductCode = "P" },
        new() { Position = 3, Title = "T3", Image = "i3", ItemType = CatalogPromotion.CataloguePageItem, PageLink = "page5", ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1) },
        new() { Position = 4, Title = "T4", Image = "i4", ItemType = CatalogPromotion.CataloguePageItem, PageLink = "page6" },
    ];

    private static CatalogSnapshotService Snapshots(ICatalogManager? catalog = null) =>
        new(catalog ?? Catalog(), TimeProvider.System);

    private static ICatalogManager Catalog(params CatalogPromotion[] promotions)
    {
        var deal = new CatalogDeal
        {
            Id = 77,
            ItemDataList =
        [
            new CatalogItem { Definition = Def(InteractionType.Badge, "b", name: "ADM") },
            new CatalogItem { Amount = 2, Definition = Def(InteractionType.None, "i", sprite: 5) },
        ]
        };

        return Proxy<ICatalogManager>((method, args) => method switch
        {
            "TryGetDeal" => Out(args, 1, (int)args[0]! == 77 ? deal : null),
            "TryGetBot" => Out(args, 1, (uint)args[0]! == 555 ? new CatalogBot { Id = 555, Figure = "hd-1.ch-2" } : null),
            "get_Promotions" => promotions,
            _ => throw new InvalidOperationException(method),
        });
    }

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
            page.Items[offerId * 10] = new CatalogItem { Id = offerId * 10, OfferId = offerId, PageId = id, Definition = Def(InteractionType.None, "i") };
        }

        new CatalogOfferIndex().Build([page]);

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
