using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class CatalogStructureWireTests
{
    private static CatalogPage Page(int id, int parentId, string mode = CatalogModes.Normal, bool enabled = true, int minimumRank = 1, params int[] offerIds)
    {
        var page = new CatalogPage { Id = id, ParentId = parentId, Enabled = enabled, Visible = true, Icon = id, Link = "page" + id, Caption = "Page " + id, Layout = "default_3x3", CatalogMode = mode, MinimumRank = minimumRank };
        foreach (var offerId in offerIds)
            page.ItemOffers[offerId] = new CatalogItem { Id = offerId * 10, OfferId = offerId };
        return page;
    }

    [Fact]
    public void IndexWritesEveryLevelForTheRequestedModeAndHidesPagesAboveTheUsersRank()
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1, Rank = 1 });
        CatalogPage[] pages =
        [
            Page(1, -1), Page(2, 1, offerIds: 7), Page(3, 2), Page(4, 3, enabled: false), Page(5, 4),
            Page(6, 1, minimumRank: 7), Page(8, -1, CatalogModes.BuildersClub)
        ];
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogIndexComposer(client, pages).Compose(packet);

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
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1, Rank = 1 });
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogIndexComposer(client, [Page(1, -1), Page(8, -1, CatalogModes.BuildersClub)], CatalogModes.FromClient("BUILDERS_CLUB")).Compose(packet);

        Assert.Equal(new List<object>
        {
            true, 0, -1, -1, "root", "", 0, 1,
            true, 8, 8, -1, "page8", "Page 8", 0, 0,
            false, "BUILDERS_CLUB"
        }, packet.Writes);
    }

    [Fact]
    public void HiddenPagesStayOpenableWhileDisabledAndRankLockedPagesDoNot()
    {
        var user = new Habbo { Id = 1, Rank = 1 };
        var hidden = Page(1, -1);
        hidden.Visible = false;

        Assert.True(hidden.CanOpen(user));
        Assert.False(Page(2, -1, enabled: false).CanOpen(user));
        Assert.False(Page(3, -1, minimumRank: 2).CanOpen(user));
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
}
