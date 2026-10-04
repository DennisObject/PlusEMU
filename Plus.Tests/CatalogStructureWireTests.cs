using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class CatalogStructureWireTests
{
    private static CatalogPage Page(int id, int parentId, string mode = CatalogModes.Normal, bool enabled = true, string? requiredPermission = null, params int[] offerIds)
    {
        var page = new CatalogPage { Id = id, ParentId = parentId, Enabled = enabled, Visible = true, Icon = id, Link = "page" + id, Caption = "Page " + id, Layout = "default_3x3", CatalogMode = mode, RequiredPermission = requiredPermission };
        foreach (var offerId in offerIds)
            page.Items[offerId * 10] = new CatalogItem { Id = offerId * 10, OfferId = offerId, PageId = id };
        new CatalogOfferIndex().Build([page]);
        return page;
    }

    [Fact]
    public void IndexWritesEveryLevelForTheRequestedModeAndHidesPagesRequiringMissingPermissions()
    {
        var (client, _) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        CatalogPage[] pages =
        [
            Page(1, -1), Page(2, 1, offerIds: 7), Page(3, 2), Page(4, 3, enabled: false), Page(5, 4),
            Page(6, 1, requiredPermission: EditorTestSupport.RestrictedPagePermission), Page(8, -1, CatalogModes.BuildersClub)
        ];
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogIndexComposer(CatalogSnapshotTestSupport.Snapshots().CaptureIndex(client.GetHabbo(), pages, CatalogModes.Normal)).Compose(packet);

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
    public void BuildersClubIndexOnlyHoldsBuildersClubPages()
    {
        var (client, _) = HabbiconTestSupport.Client(EditorTestSupport.Player());
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogIndexComposer(CatalogSnapshotTestSupport.Snapshots().CaptureIndex(client.GetHabbo(), [Page(1, -1), Page(8, -1, CatalogModes.BuildersClub)], CatalogModes.FromClient("BUILDERS_CLUB"))).Compose(packet);

        Assert.Equal(new List<object>
        {
            true, 0, -1, -1, "root", "", 0, 1,
            true, 8, 8, -1, "page8", "Page 8", 0, 0,
            false, "BUILDERS_CLUB"
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
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var page = new CatalogPromotion { Position = 2, Title = "Offer", Image = "a.png", ItemType = CatalogPromotion.ProductOfferItem, OfferId = 77, ExpiresAt = (int)now + 60 };

        Assert.False(page.HasExpired(now));
        Assert.InRange(page.SecondsLeft(now), 59, 60);
        Assert.True(new CatalogPromotion { ExpiresAt = (int)now - 1 }.HasExpired(now));
        Assert.Equal(0, new CatalogPromotion().SecondsLeft(now));
    }

    private static CatalogItem Item(int id, int offerId, int pageId) => new() { Id = id, OfferId = offerId, PageId = pageId };

    [Fact]
    public void PagesKeepOfficialOfferIdsAndGiveEveryOtherRowAUniqueId()
    {
        // Row 4 sells offer 5 and row 5 sells offer 18: buying "5" must return row 4, the one the page showed as 5.
        var page = Page(55, -1);
        page.Items = new()
        {
            [4] = Item(4, 5, 55), [5] = Item(5, 18, 55), [6] = Item(6, -1, 55),
            [18] = Item(18, -1, 55), [827] = Item(827, 590, 55), [828] = Item(828, 590, 55)
        };
        var index = new CatalogOfferIndex();

        index.Build([page]);

        Assert.Equal(6, page.Offers.Count);
        Assert.True(index.TryGet(590, EditorTestSupport.Player(), out _, out var shared));
        Assert.Equal(827, shared.Id);
        Assert.Equal(4, page.Offers[5].Id);
        Assert.Equal(5, page.Offers[18].Id);
        Assert.Equal(6, page.Offers[6].Id);
        // A legacy row whose id is an official offer id on the page moves aside instead of shadowing it.
        Assert.Equal(18, page.Offers[CatalogOfferIndex.ClashingRowIdBase + 18].Id);
        // The first row naming a shared offer id keeps it; later ones keep their row ids.
        Assert.Equal(827, page.Offers[590].Id);
        Assert.Equal(828, page.Offers[828].Id);
        Assert.Equal([5, 18, 590], CatalogOfferIndex.OfficialOfferIds(page).Order());
    }

    [Fact]
    public void SharedOffersStayOnEveryPageAndLookupsSkipPagesTheUserCannotOpen()
    {
        var user = EditorTestSupport.Player();
        var staff = Page(1, -1, requiredPermission: EditorTestSupport.RestrictedPagePermission);
        var normal = Page(2, -1);
        var builders = Page(3, -1, CatalogModes.BuildersClub);
        staff.Items = new() { [10] = Item(10, 6, 1) };
        normal.Items = new() { [20] = Item(20, 6, 2) };
        builders.Items = new() { [30] = Item(30, 6, 3) };
        var index = new CatalogOfferIndex();
        index.Build([staff, normal, builders]);

        Assert.All(new[] { staff, normal, builders }, page => Assert.True(page.Offers.ContainsKey(6)));
        Assert.True(index.TryGet(6, user, out var found, out var item));
        Assert.Equal(2, found.Id);
        Assert.Equal(20, item.Id);
        Assert.False(index.TryGet(99, user, out _, out _));
    }
}
