using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class CatalogStructureWireTests
{
    [Fact]
    public void UserPerksDoNotAdvertiseBuildersClub()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();

        new UserPerksComposer(false).Compose(packet);

        Assert.Equal(14, packet.Writes[0]);
        Assert.DoesNotContain("BUILDER_AT_WORK", packet.Writes);
    }

    [Fact]
    public async Task UnsupportedCatalogModeFallsBackToTheNormalIndex()
    {
        var pages = new[] { Page(1, -1) };
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) => method == "get_Pages" ? pages : throw new InvalidOperationException(method));
        var (client, sent) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        var packet = HabbiconTestSupport.Incoming("BUILDERS_CLUB");

        var service = new CatalogBrowsingService(null!, null!, null!, TimeProvider.System, catalog, null!, CatalogSnapshotTestSupport.Snapshots());
        await new GetCatalogIndexEvent(service).Parse(client, packet);

        Assert.False(packet.HasDataRemaining());
        Assert.Equal([ServerPacketHeader.CatalogIndexComposer, ServerPacketHeader.CatalogItemDiscountComposer], sent.Select(value => value.Header));
    }

    [Fact]
    public async Task EmptyBundleDiscountRequestRepliesWithTheRuleset()
    {
        var (client, sent) = HabbiconTestSupport.Client(EditorTestSupport.Player());

        await new GetBundleDiscountRulesetEvent().Parse(client, HabbiconTestSupport.Incoming());

        Assert.Equal([ServerPacketHeader.CatalogItemDiscountComposer], sent.Select(value => value.Header));
    }

    [Fact]
    public async Task BrowserRevisionsRouteBundleDiscountAndIndexRequestsToTheirOwnHandlers()
    {
        var directory = Directory.CreateTempSubdirectory("catalog-revisions-").FullName;

        try {
            foreach (var file in Directory.GetFiles(HabbiconPacketTests.Repo("Resources/Revisions"), "*.json")) {
                File.Copy(file, Path.Join(directory, Path.GetFileName(file)));
            }

            var cache = new Plus.Communication.Revisions.RevisionsCache();
            typeof(Plus.Communication.Revisions.RevisionsCache).GetField("_directory",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(cache, directory);
            await cache.Start();

            foreach (var name in new[] { "NITRO-1-6-6", "NITRO-3-6-0", "OCTANE-3-6-0-FLOOR-20260909" }) {
                var revision = cache.Revisions[name];
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.GetBundleDiscountRulesetEvent, revision.IncomingIdToInternalIdMapping[223]);
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.GetCatalogModeEvent, revision.IncomingIdToInternalIdMapping[1195]);
                Assert.Equal(2347u, revision.InternalIdToOutgoingIdMapping[ServerPacketHeader.CatalogItemDiscountComposer]);
            }
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CatalogPage Page(int id, int parentId, bool enabled = true, string? requiredPermission = null, params int[] offerIds)
    {
        var page = new CatalogPage { Id = id, ParentId = parentId, Enabled = enabled, Visible = true, Icon = id, Link = "page" + id, Caption = "Page " + id, Layout = "default_3x3", RequiredPermission = requiredPermission };

        foreach (var offerId in offerIds) {
            page.Offers[offerId] = new CatalogOffer { Id = offerId };
        }

        return page;
    }

    [Fact]
    public void IndexWritesEveryLevelForTheRequestedModeAndHidesPagesRequiringMissingPermissions()
    {
        var (client, _) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        CatalogPage[] pages =
        [
            Page(1, -1), Page(2, 1, offerIds: 7), Page(3, 2), Page(4, 3, enabled: false), Page(5, 4),
            Page(6, 1, requiredPermission: EditorTestSupport.RestrictedPagePermission)
        ];
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogIndexComposer(CatalogSnapshotTestSupport.Snapshots().CaptureIndex(client.GetHabbo(), pages)).Compose(packet);

        Assert.Equal(new List<object>
        {
            true, 0, -1, -1, "root", "", 0, 1,
            true, 1, 1, -1, "page1", "Page 1", 0, 1,
            true, 2, 2, 1, "page2", "Page 2", 1, 7, 1,
            true, 3, 3, 2, "page3", "Page 3", 0, 1,
            true, 4, -1, 3, "page4", "Page 4", 0, 1,
            true, 5, 5, 4, "page5", "Page 5", 0, 0,
            false, "NORMAL"
        }, packet.Writes);
    }

    [Fact]
    public void HiddenPagesStayOpenableWhileDisabledAndPermissionLockedPagesDoNot()
    {
        var user = EditorTestSupport.Player();
        var hidden = Page(1, -1);
        hidden.Visible = false;

        Assert.True(hidden.CanOpen(user));
        Assert.False(Page(2, -1, enabled: false).CanOpen(user));
        Assert.False(Page(3, -1, requiredPermission: EditorTestSupport.RestrictedPagePermission).CanOpen(user));
    }

    [Fact]
    public void PageAccessFollowsPermissionGrantsRegardlessOfRoleWeight()
    {
        var page = Page(3, -1, requiredPermission: EditorTestSupport.RestrictedPagePermission);
        var lowWeight = new Habbo { Access = EditorTestSupport.Access([EditorTestSupport.RestrictedPagePermission], weight: 1) };
        var highWeight = new Habbo { Access = EditorTestSupport.Access([], weight: 1000) };

        Assert.True(page.CanOpen(lowWeight));
        Assert.False(page.CanOpen(highWeight));
    }

    [Fact]
    public void FrontPagePromotionsWriteTheFieldTheirTypeNeeds()
    {
        var now = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));
        var page = new CatalogPromotion { Position = 2, Title = "Offer", Image = "a.png", ItemType = CatalogPromotion.ProductOfferItem, OfferId = 77, ExpiresAt = now.AddSeconds(60) };

        Assert.False(page.HasExpiredAt(now));
        Assert.Equal(TimeSpan.FromMinutes(1), page.RemainingAt(now));
        Assert.True(new CatalogPromotion { ExpiresAt = now.AddSeconds(-1) }.HasExpiredAt(now));
        Assert.Equal(TimeSpan.Zero, new CatalogPromotion().RemainingAt(now));
    }

    [Fact]
    public void SharedOffersStayOnEveryPageAndLookupsSkipPagesTheUserCannotOpen()
    {
        var user = EditorTestSupport.Player();
        var staff = Page(1, -1, requiredPermission: EditorTestSupport.RestrictedPagePermission);
        var normal = Page(2, -1);
        var shared = new CatalogOffer { Id = 6 };
        staff.Offers[6] = shared;
        normal.Offers[6] = shared;
        var index = new CatalogOfferIndex();
        index.Build([staff, normal]);

        Assert.True(index.TryGet(6, user, out var found, out var offer));
        Assert.Equal(2, found.Id);
        Assert.Same(shared, offer);
        Assert.False(index.TryGet(99, user, out _, out _));
    }
}
