using System.Diagnostics.CodeAnalysis;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogBrowsingSnapshotTests
{
    [Fact]
    public void PetPalettesKeepTheirLegacyFieldsAndFreezeRaceData()
    {
        var race = new PetRace(1, 2, 3, true, true);
        var source = new List<PetRace> { race };
        var composer = new SellablePetBreedsComposer(PetPaletteSnapshot.Capture("pet", 7, source));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { "pet", 1, 7, 2, 3, true, false, false }, before.Writes);
        race.PrimaryColour = 99;
        race.SecondaryColour = 99;
        source.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public void PromotableRoomsUseInjectedOwnerLoaderAndOneCapturedExpiryInstant()
    {
        var now = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new CountingClock(now);
        var exact = new RoomData { Id = 2, Name = "expired", Promotion = new("p", "d", 0, now.AddHours(-1), now, new ThrowingClock()) };
        var loader = new Rooms
        {
            Data = [new() { Id = 1, Name = "available" }, exact,
                new() { Id = 3, Name = "active", Promotion = new("p", "d", 0, now, now.AddMinutes(1), new ThrowingClock()) }]
        };
        var service = new CatalogBrowsingService(null!, null!, loader, clock, null!, null!, null!);
        var composer = new PromotableRoomsComposer(service.CapturePromotableRooms(42));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { true, 2, 1u, "available", false, 2u, "expired", false }, before.Writes);
        Assert.Equal(42, loader.OwnerId);
        Assert.Equal(1, clock.Reads);
        exact.Name = "changed";
        loader.Data.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public async Task BrowsingHandlersOnlyDecodeAndDelegate()
    {
        var service = new RecordingBrowsing();
        await new GetSellablePetPalettesEvent(service).Parse(null!, HabbiconTestSupport.Incoming("pet"));
        await new GetPromotableRoomsEvent(service).Parse(null!, null!);
        var page = HabbiconTestSupport.Incoming(12, 34, "BUILDERS_CLUB");
        var index = HabbiconTestSupport.Incoming("BUILDERS_CLUB");
        var mode = HabbiconTestSupport.Incoming("BUILDERS_CLUB");
        await new GetCatalogPageEvent(service).Parse(null!, page);
        await new GetCatalogIndexEvent(service).Parse(null!, index);
        await new GetCatalogModeEvent(service).Parse(null!, mode);
        var offer = HabbiconTestSupport.Incoming(44);
        await new GetClubOffersEvent(service).Parse(null!, offer);
        Assert.Equal(44, service.OfferId);
        Assert.False(offer.HasDataRemaining());
        Assert.Equal("pet", service.Type);
        Assert.True(service.PromotableRequested);
        Assert.Equal(new CatalogPageRequest(12, 34, "BUILDERS_CLUB"), service.PageRequest);
        Assert.Equal(["index:BUILDERS_CLUB", "mode:BUILDERS_CLUB"], service.Modes);
        Assert.False(page.HasDataRemaining());
        Assert.False(index.HasDataRemaining());
        Assert.False(mode.HasDataRemaining());
    }

    [Fact]
    public async Task PageHandlerDoesNotDelegateATruncatedFrame()
    {
        var service = new RecordingBrowsing();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new GetCatalogPageEvent(service).Parse(null!, HabbiconTestSupport.Incoming(12, 34)));

        Assert.Null(service.PageRequest);
    }

    [Fact]
    public void PageBrowsingValidatesBeforeAdminSnapshotAndPublication()
    {
        var page = new CatalogPage
        {
            Id = 7, Enabled = true, Visible = false, Layout = "frontpage",
            Offers = { [44] = new CatalogItem { Id = 1, OfferId = 44, PageId = 7 } }
        };
        var found = true;
        var pages = new List<CatalogPage> { page };
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, args) => method switch
        {
            nameof(ICatalogManager.TryGetPage) => TryPage(args, found ? page : null),
            "get_Pages" => pages,
            "get_Promotions" => Array.Empty<CatalogPromotion>(),
            _ => throw new InvalidOperationException(method)
        });
        var viewed = new List<int>();
        var admin = CatalogSnapshotTestSupport.Proxy<ICatalogAdminService>((method, args) =>
        {
            if (method == nameof(ICatalogAdminService.RecordViewedPage))
            {
                viewed.Add((int)args[1]!);
                return null;
            }
            throw new InvalidOperationException(method);
        });
        var snapshots = new RecordingSnapshots(new CatalogSnapshotService(catalog, TimeProvider.System));
        var service = new CatalogBrowsingService(null!, null!, null!, TimeProvider.System, catalog, admin, snapshots);
        var (client, sent) = HabbiconTestSupport.Client(EditorTestSupport.Player());

        service.ShowPage(client, new(7, 44, "IGNORED"));

        Assert.Equal([7], viewed);
        Assert.Equal([44], snapshots.PageOffers);
        Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.CatalogPageComposer, sent[0].Header);
        var (expectedClient, expectedSent) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        expectedClient.Send(new CatalogPageComposer(new CatalogSnapshotService(catalog, TimeProvider.System).CapturePage(page, 44)));
        Assert.Equal(expectedSent[0].Payload, sent[0].Payload);

        service.ShowPage(client, new(7, 999, "NORMAL"));
        Assert.Equal([44, -1], snapshots.PageOffers);

        page.Enabled = false;
        service.ShowPage(client, new(7, 44, "NORMAL"));
        page.Enabled = true;
        page.RequiredPermission = EditorTestSupport.RestrictedPagePermission;
        service.ShowPage(client, new(7, 44, "NORMAL"));
        found = false;
        service.ShowPage(client, new(7, 44, "NORMAL"));

        Assert.Equal(2, viewed.Count);
        Assert.Equal(2, snapshots.PageOffers.Count);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void IndexAndModeUseNormalSnapshotWithLegacyPublicationOrder()
    {
        var pages = new List<CatalogPage>();
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) => method switch
        {
            "get_Pages" => pages,
            _ => throw new InvalidOperationException(method)
        });
        var snapshots = new RecordingSnapshots(CatalogSnapshotTestSupport.Snapshots());
        var service = new CatalogBrowsingService(null!, null!, null!, TimeProvider.System, catalog, null!, snapshots);
        var (client, sent) = HabbiconTestSupport.Client(EditorTestSupport.Player());

        service.ShowIndex(client, "BUILDERS_CLUB");
        service.ShowMode(client, "UNKNOWN");

        Assert.Equal(2, snapshots.IndexCaptures);
        Assert.Equal(
            [ServerPacketHeader.CatalogIndexComposer, ServerPacketHeader.CatalogItemDiscountComposer, ServerPacketHeader.CatalogIndexComposer],
            sent.Select(packet => packet.Header));
    }

    [Fact]
    public void OfferBrowsingCapturesOnlyAnOfferAllowedByTheCanonicalCatalog()
    {
        var item = new CatalogItem { Id = 1, OfferId = 44, Amount = 1,
            Definition = new ItemDefinition { ItemName = "chair", SpriteId = 3, Type = ItemType.Floor } };
        var found = true;
        var (client, sent) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, args) =>
        {
            Assert.Equal(nameof(ICatalogManager.TryGetOffer), method);
            Assert.Equal(44, args[0]);
            Assert.Same(client.GetHabbo(), args[1]);
            args[2] = found ? new CatalogPage() : null;
            args[3] = found ? item : null;
            return found;
        });
        var snapshots = new RecordingSnapshots(CatalogSnapshotTestSupport.Snapshots());
        var service = new CatalogBrowsingService(null!, null!, null!, TimeProvider.System, catalog, null!, snapshots);

        service.ShowOffer(client, 44);

        Assert.Equal(1, snapshots.OfferCaptures);
        Assert.Equal(ServerPacketHeader.CatalogOfferComposer, Assert.Single(sent).Header);
        var (expectedClient, expectedSent) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        expectedClient.Send(new CatalogOfferComposer(CatalogSnapshotTestSupport.Snapshots().CaptureOffer(item)));
        Assert.Equal(expectedSent[0].Payload, sent[0].Payload);

        found = false;
        service.ShowOffer(client, 44);
        Assert.Equal(1, snapshots.OfferCaptures);
        Assert.Single(sent);
    }

    [Fact]
    public async Task OfferHandlerDoesNotDelegateATruncatedIdentifier()
    {
        var service = new RecordingBrowsing();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new GetClubOffersEvent(service).Parse(null!, HabbiconTestSupport.Incoming()));
        Assert.Null(service.OfferId);
    }

    private static bool TryPage(object?[] args, CatalogPage? page)
    {
        args[1] = page;
        return page != null;
    }

    private sealed class Rooms : IRoomDataLoader
    {
        public List<RoomData> Data { get; set; } = [];
        public int OwnerId { get; private set; }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId) { OwnerId = ownerId; return Data; }
        public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data) => throw new NotSupportedException();
    }
    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }
    private sealed class ThrowingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Expiry must use the captured clock.");
    }
    private sealed class RecordingBrowsing : ICatalogBrowsingService
    {
        public string? Type { get; private set; }
        public bool PromotableRequested { get; private set; }
        public CatalogPageRequest? PageRequest { get; private set; }
        public List<string> Modes { get; } = [];
        public int? OfferId { get; private set; }
        public void ShowOffer(GameClient session, int offerId) => OfferId = offerId;
        public void ShowPetPalettes(GameClient session, string type) => Type = type;
        public void ShowPromotableRooms(GameClient session) => PromotableRequested = true;
        public void ShowPage(GameClient session, CatalogPageRequest request) => PageRequest = request;
        public void ShowIndex(GameClient session, string mode) => Modes.Add("index:" + mode);
        public void ShowMode(GameClient session, string mode) => Modes.Add("mode:" + mode);
    }

    private sealed class RecordingSnapshots(ICatalogSnapshotService inner) : ICatalogSnapshotService
    {
        public List<int> PageOffers { get; } = [];
        public int IndexCaptures { get; private set; }
        public int OfferCaptures { get; private set; }
        public CatalogOfferSnapshot CaptureOffer(CatalogItem item) { OfferCaptures++; return inner.CaptureOffer(item); }
        public CatalogPageSnapshot CapturePage(CatalogPage page, int preselectOfferId)
        {
            PageOffers.Add(preselectOfferId);
            return inner.CapturePage(page, preselectOfferId);
        }
        public CatalogIndexSnapshot CaptureIndex(Habbo habbo, ICollection<CatalogPage> pages)
        {
            IndexCaptures++;
            return inner.CaptureIndex(habbo, pages);
        }
        public ClubGiftsSnapshot CaptureClubGifts(ClubGiftInfo info) => inner.CaptureClubGifts(info);
    }
}
