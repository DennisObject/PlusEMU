using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class ClubCatalogServiceTests
{
    [Fact]
    public async Task HandlersDecodePrimitiveRequestsAndDelegate()
    {
        var service = new RecordingClubCatalogService();

        await new GetClubGiftInfoEvent(service).Parse(null!, HabbiconTestSupport.Incoming());
        await new SelectClubGiftEvent(service).Parse(null!, HabbiconTestSupport.Incoming("gift_code"));
        await new PurchaseBasicMembershipExtensionEvent(service).Parse(null!, HabbiconTestSupport.Incoming(17));
        await new PurchaseVipMembershipExtensionEvent(service).Parse(null!, HabbiconTestSupport.Incoming(23));

        Assert.Equal(1, service.GiftViews);
        Assert.Equal("gift_code", service.ProductCode);
        Assert.Equal(new[] { 17, 23 }, service.OfferIds);
    }

    [Fact]
    public async Task FailedGiftClaimPublishesOnlyUnavailableError()
    {
        var context = Context(claimSucceeds: false);

        await context.Service.ClaimGift(context.Client, "missing");

        Assert.Equal(1, context.Rewards.Claims);
        Assert.Equal(new[] { ServerPacketHeader.PurchaseErrorComposer }, Headers(context.Sent));
    }

    [Fact]
    public async Task SuccessfulGiftClaimPublishesCommittedGiftBeforeInventoryAndUpdatedStatus()
    {
        var context = Context();

        await context.Service.ClaimGift(context.Client, "club_chair");

        Assert.Equal(new uint[]
        {
            ServerPacketHeader.ClubGiftReceivedComposer,
            ServerPacketHeader.FurniListNotificationComposer,
            ServerPacketHeader.FurniListUpdateComposer,
            ServerPacketHeader.ClubGiftsComposer,
            ServerPacketHeader.PickMonthlyClubGiftComposer
        }, Headers(context.Sent));
        Assert.Equal(1, context.Rewards.GiftReads);
    }

    [Fact]
    public async Task MissingClubPageOrOfferAndWalletRefusalPublishNoSuccess()
    {
        var missingPage = Context(hasClubPage: false);
        await missingPage.Service.PurchaseMembership(missingPage.Client, 9);
        Assert.Equal(new[] { ServerPacketHeader.PurchaseErrorComposer }, Headers(missingPage.Sent));
        Assert.Equal(0, missingPage.Memberships.Purchases);

        var missingOffer = Context(hasOffer: false);
        await missingOffer.Service.PurchaseMembership(missingOffer.Client, 9);
        Assert.Equal(new[] { ServerPacketHeader.PurchaseErrorComposer }, Headers(missingOffer.Sent));
        Assert.Equal(0, missingOffer.Memberships.Purchases);

        var refused = Context(membershipSucceeds: false);
        await refused.Service.PurchaseMembership(refused.Client, 9);
        Assert.Equal(new[] { ServerPacketHeader.PurchaseErrorComposer }, Headers(refused.Sent));
        Assert.Equal(1, refused.Memberships.Purchases);
    }

    [Fact]
    public async Task SuccessfulMembershipExtensionPublishesBalancesBeforeConfirmationAndMembership()
    {
        var context = Context();

        await context.Service.PurchaseMembership(context.Client, 9);

        Assert.Equal(new uint[]
        {
            ServerPacketHeader.CreditBalanceComposer,
            ServerPacketHeader.HabboActivityPointNotificationComposer,
            ServerPacketHeader.PurchaseOKComposer,
            ServerPacketHeader.ScrSendUserInfoComposer
        }, Headers(context.Sent));
        Assert.Equal(90, context.Habbo.Credits);
        Assert.Equal(45, context.Habbo.Diamonds);
    }

    [Fact]
    public void ClubGiftReceivedComposerUsesOnlyCapturedScalars()
    {
        var definition = Definition();
        var item = Item(definition);
        var gift = new ClubGiftReceivedSnapshot(
            item.CatalogName,
            item.Definition.ProductType,
            item.Definition.SpriteId,
            item.Amount);
        var first = new HabbiconTestSupport.RecordingPacket();
        new ClubGiftReceivedComposer(gift).Compose(first);

        item.CatalogName = "changed";
        item.Definition.ProductType = "i";
        item.Definition.SpriteId = 999;
        item.Amount = 99;
        var second = new HabbiconTestSupport.RecordingPacket();
        new ClubGiftReceivedComposer(gift).Compose(second);

        Assert.Equal(first.Writes, second.Writes);
        Assert.Equal(new object[] { "club_chair", 1, "s", 50, "", 1, false }, second.Writes);
    }

    private static TestContext Context(
        bool claimSucceeds = true,
        bool hasClubPage = true,
        bool hasOffer = true,
        bool membershipSucceeds = true)
    {
        var habbo = new Habbo { Id = 42, Username = "Dennis", Credits = 100, Diamonds = 50 };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var rewards = new RecordingRewards(claimSucceeds);
        var catalog = DispatchProxy.Create<ICatalogManager, CatalogProxy>();
        var catalogProxy = (CatalogProxy)(object)catalog;
        catalogProxy.HasOffer = hasOffer;
        catalogProxy.Offer = new ClubOffer
        {
            Id = 9,
            Name = "HC_31",
            Days = 31,
            Credits = 10,
            Points = 5,
            PointsType = 5
        };
        catalogProxy.Pages = hasClubPage
            ? [new CatalogPage { Id = 1, Enabled = true, Layout = "club_buy" }]
            : [];
        var memberships = new RecordingMembership(membershipSucceeds);
        var service = new ClubCatalogService(rewards, new SnapshotService(), catalog, memberships);
        return new(service, client, habbo, sent, rewards, memberships);
    }

    private static uint[] Headers(List<(uint Header, byte[] Payload)> sent) =>
        sent.Select(packet => packet.Header).ToArray();

    private static ItemDefinition Definition() => new()
    {
        Id = 20,
        ItemName = "club_chair",
        ProductType = "s",
        SpriteId = 50,
        Type = ItemType.Floor,
        InteractionType = InteractionType.None
    };

    private static CatalogItem Item(ItemDefinition definition) => new()
    {
        Id = 7,
        OfferId = 70,
        CatalogName = "club_chair",
        Definition = definition,
        Amount = 1,
        HaveOffer = true
    };

    private sealed record TestContext(
        ClubCatalogService Service,
        GameClient Client,
        Habbo Habbo,
        List<(uint Header, byte[] Payload)> Sent,
        RecordingRewards Rewards,
        RecordingMembership Memberships);

    private sealed class RecordingClubCatalogService : IClubCatalogService
    {
        public int GiftViews { get; private set; }
        public string? ProductCode { get; private set; }
        public List<int> OfferIds { get; } = [];

        public Task ShowStatus(GameClient session, string type) => Task.CompletedTask;
        public Task ShowKickback(GameClient session) => Task.CompletedTask;

        public Task ShowGifts(GameClient session)
        {
            GiftViews++;
            return Task.CompletedTask;
        }

        public Task ClaimGift(GameClient session, string productCode)
        {
            ProductCode = productCode;
            return Task.CompletedTask;
        }

        public Task PurchaseMembership(GameClient session, int offerId)
        {
            OfferIds.Add(offerId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRewards(bool claimSucceeds) : IClubRewards
    {
        private readonly CatalogItem _item = Item(Definition());
        public int Claims { get; private set; }
        public int GiftReads { get; private set; }

        public ClubGiftInfo Gifts(Habbo habbo)
        {
            GiftReads++;
            return new ClubGiftInfo(3, 1, 40, [new ClubGift(_item, 1)]);
        }

        public ClubGiftClaim? Claim(Habbo habbo, string productCode)
        {
            Claims++;
            return claimSucceeds
                ? new ClubGiftClaim(new ClubGift(_item, 1), [new InventoryItem { Id = 700 }])
                : null;
        }

        public bool Charge(Habbo habbo, int credits, int duckets = 0, int diamonds = 0,
            Func<IDbConnection, IDbTransaction, bool>? deliver = null, bool kickbackEligible = true) =>
            throw new NotSupportedException();

        public ClubKickback Kickback(Habbo habbo) => throw new NotSupportedException();
        public void RunPaydays() => throw new NotSupportedException();
    }

    private sealed class RecordingMembership(bool succeeds) : IClubMembershipService
    {
        public int Purchases { get; private set; }
        public DateTimeOffset? GetExpiry(int userId) => null;

        public DateTimeOffset? Purchase(Habbo habbo, ClubOffer offer, int? recipientId = null)
        {
            Purchases++;
            if (!succeeds)
                return null;
            habbo.Credits -= offer.Credits;
            habbo.Diamonds -= offer.Points;
            return new DateTimeOffset(2040, 3, 5, 4, 5, 6, TimeSpan.Zero);
        }

        public DateTimeOffset? Grant(Habbo actor, int userId, int days) => throw new NotSupportedException();
    }

    public class CatalogProxy : DispatchProxy
    {
        public bool HasOffer { get; set; }
        public ClubOffer Offer { get; set; } = null!;
        public ICollection<CatalogPage> Pages { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Pages")
                return Pages;
            if (targetMethod?.Name == nameof(ICatalogManager.TryGetClubOffer))
            {
                args![1] = HasOffer ? Offer : null;
                return HasOffer && (int)args[0]! == Offer.Id;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class SnapshotService : ICatalogSnapshotService
    {
        public ClubGiftsSnapshot CaptureClubGifts(ClubGiftInfo info) => new(
            info.DaysUntilNextGift,
            info.Available,
            ImmutableArray<CatalogOfferSnapshot>.Empty,
            ImmutableArray<ClubGiftEntry>.Empty);

        public CatalogOfferSnapshot CaptureOffer(CatalogItem item) => throw new NotSupportedException();
        public CatalogPageSnapshot CapturePage(CatalogPage page, int preselectOfferId) => throw new NotSupportedException();
        public CatalogIndexSnapshot CaptureIndex(Habbo habbo, ICollection<CatalogPage> pages) => throw new NotSupportedException();
    }
}
