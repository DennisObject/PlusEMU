using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public class CatalogAdminValidationTests
{
    private static readonly CatalogPageRow Root = new() { Id = 1, ParentId = -1, Caption = "Shop", RequiredPermission = null };
    private static readonly CatalogPageRow Child = new() { Id = 2, ParentId = 1, Caption = "Child", RequiredPermission = null };
    private static readonly CatalogPageRow Staff = new() { Id = 3, ParentId = -1, Caption = "Staff", RequiredPermission = EditorTestSupport.RestrictedPagePermission };

    private static readonly CatalogAdminPage Valid = new("NORMAL", 0, 1, "new_page", "New page", "default_3x3", 1, 10, "", 0, true, true, false,
        "NORMAL", "header", "", "", "text", "", "", "", 0, "");

    private static readonly CatalogAdminOffer Offer = new("NORMAL", 0, "10", 1, "chair", 3, 0, 0, 1, 0, -1, -1, 0, "", true, false);

    private static CatalogPageRow? Find(int id) => new[] { Root, Child, Staff }.FirstOrDefault(page => page.Id == id);

    private static Dictionary<string, string> Check(CatalogAdminPage page, CatalogPageRow? existing = null, UserAccess? access = null) =>
        CatalogAdminValidation.Page(page, existing, access ?? EditorTestSupport.Staff().Access, Find);

    [Fact]
    public void AcceptsAValidPage() => Assert.Empty(Check(Valid));

    [Theory]
    [InlineData("caption", "")]
    [InlineData("caption", "bad\u0001caption")]
    [InlineData("captionSave", "bad link")]
    [InlineData("pageLayout", "../../etc")]
    [InlineData("pageText1", "two|texts")]
    public void RejectsBadText(string field, string value)
    {
        var page = field switch
        {
            "caption" => Valid with { Caption = value },
            "captionSave" => Valid with { CaptionSave = value },
            "pageLayout" => Valid with { PageLayout = value },
            _ => Valid with { PageText1 = value }
        };
        Assert.Contains(field, Check(page).Keys);
    }

    [Fact]
    public void RejectsOverlongTextAndPermissionsTheEditorDoesNotHave()
    {
        Assert.Contains("caption", Check(Valid with { Caption = new string('a', 129) }).Keys);
        Assert.Contains("pageText1", Check(Valid with { PageText1 = new string('a', 8193) }).Keys);
        Assert.Contains("requiredPermission", Check(Valid with { RequiredPermission = EditorTestSupport.RestrictedPagePermission }).Keys);
        Assert.Contains("requiredPermission", Check(Valid with { RequiredPermission = "*" }).Keys);
        Assert.Contains("requiredPermission", Check(Valid with { RequiredPermission = null! }).Keys);
        Assert.Contains("_form", Check(Valid with { PageId = 3, ParentId = -1 }, Staff).Keys);
    }

    [Fact]
    public void EditorsCanUseOnlyPermissionsTheyHoldRegardlessOfRoleWeight()
    {
        var held = Valid with { RequiredPermission = EditorTestSupport.RestrictedPagePermission };
        var lowWeight = EditorTestSupport.Access([EditorTestSupport.RestrictedPagePermission], weight: 1);
        var highWeight = EditorTestSupport.Access([PermissionKeys.CatalogEdit], weight: 1000);

        Assert.Empty(Check(held, access: lowWeight));
        Assert.Contains("requiredPermission", Check(held, access: highWeight).Keys);
        Assert.Contains("parentId", Check(Valid with { ParentId = 3 }, access: highWeight).Keys);
        Assert.Empty(Check(Valid with { ParentId = 3 }, access: lowWeight));
    }

    [Theory]
    [InlineData("catalog.*")]
    [InlineData("bad permission")]
    [InlineData("catalog..edit")]
    [InlineData("Catalog.Edit")]
    public void RequiredPermissionMustBeAConcreteKeyEvenWhenTheActorHoldsIt(string key)
    {
        Assert.Contains("requiredPermission", Check(Valid with { RequiredPermission = key }, access: EditorTestSupport.Access([key])).Keys);
    }

    [Fact]
    public void ParentMustExistStayInTheCatalogAndNotBeADescendant()
    {
        Assert.Contains("parentId", Check(Valid with { ParentId = 99 }).Keys);
        Assert.Contains("parentId", Check(Valid with { PageId = 1, ParentId = 2 }, Root).Keys);
        Assert.Contains("parentId", Check(Valid with { PageId = 1, ParentId = 1 }, Root).Keys);
        Assert.Empty(Check(Valid with { ParentId = -1 }));
    }

    [Fact]
    public void UnsupportedFieldsAreRefusedInsteadOfDropped()
    {
        var errors = Check(Valid with { ClubOnly = true, RoomId = 5, Includes = "1;2", CatalogMode = "BOTH" });
        Assert.Equal(["catalogMode", "clubOnly", "includes", "roomId"], errors.Keys.Order());
        Assert.Contains("catalogMode", Check(Valid with { PageId = 2, CatalogMode = "BUILDER" }, Child).Keys);
    }

    [Fact]
    public void OfferRulesCoverItemsPricesAndLimits()
    {
        Dictionary<string, string> CheckOffer(CatalogAdminOffer offer, CatalogOfferRow? existing = null) =>
            CatalogAdminValidation.Offer(offer, existing, EditorTestSupport.Staff().Access, Find, id => id == 10);

        Assert.Empty(CheckOffer(Offer));
        Assert.Contains("itemIds", CheckOffer(Offer with { ItemIds = "11" }).Keys);
        Assert.Contains("itemIds", CheckOffer(Offer with { ItemIds = "10;11" }).Keys);
        Assert.Contains("pageId", CheckOffer(Offer with { PageId = 99 }).Keys);
        Assert.Contains("pageId", CheckOffer(Offer with { PageId = 3 }).Keys);
        Assert.Contains("costCredits", CheckOffer(Offer with { CostCredits = -1 }).Keys);
        Assert.Contains("pointsType", CheckOffer(Offer with { PointsType = 101 }).Keys);
        Assert.Contains("amount", CheckOffer(Offer with { Amount = 0 }).Keys);
        Assert.Contains("songId", CheckOffer(Offer with { SongId = 3 }).Keys);
        var sold = new CatalogOfferRow { Id = 5, PageId = 1, ItemId = "10", LimitedStack = 100, LimitedSells = 40 };
        Assert.Contains("limitedStack", CheckOffer(Offer with { LimitedStack = 39 }, sold).Keys);
        Assert.Empty(CheckOffer(Offer with { LimitedStack = 40 }, sold));
        var habbicon = new CatalogOfferRow { Id = 6, PageId = 1, ItemId = "0", HabbiconId = 61 };
        Assert.Empty(CheckOffer(Offer with { ItemIds = "0" }, habbicon));
        Assert.Contains("itemIds", CheckOffer(Offer with { ItemIds = "10" }, habbicon).Keys);
    }

    [Fact]
    public void PageStringsKeepPositionsTheEditorDoesNotKnow()
    {
        var row = new CatalogPageRow
        {
            Id = 9,
            ParentId = -1,
            Caption = "Old",
            PageLink = "old",
            PageLayout = "default_3x3",
            RequiredPermission = EditorTestSupport.RestrictedPagePermission,
            PageStrings1 = "head|teaser",
            PageStrings2 = "one|two|details|teaser text|fifth"
        };

        var page = CatalogAdminMapping.ToPage(row);
        Assert.Equal(("NORMAL", "head", "teaser", "", "one", "two", "details", "teaser text", EditorTestSupport.RestrictedPagePermission),
            (page.CatalogType, page.PageHeadline, page.PageTeaser, page.PageSpecial, page.PageText1, page.PageText2, page.PageTextDetails, page.PageTextTeaser, page.RequiredPermission));

        var saved = CatalogAdminMapping.Apply(page with { PageText1 = "new one", OrderNum = -1 }, row);
        Assert.Equal("head|teaser", saved.PageStrings1);
        Assert.Equal("new one|two|details|teaser text|fifth", saved.PageStrings2);
        Assert.Equal((EditorTestSupport.RestrictedPagePermission, 0), (saved.RequiredPermission, saved.OrderNum));
        Assert.Equal("new|teaser", CatalogAdminMapping.WithImages(row, "new", "teaser").PageStrings1);
        Assert.Null(CatalogAdminMapping.Apply(page with { RequiredPermission = "" }, row).RequiredPermission);
    }

    [Fact]
    public void OfferPointsMapToDucketsOrDiamonds()
    {
        var row = CatalogAdminMapping.Apply(Offer with { CostPoints = 25, PointsType = 5, ClubOnly = true, OfferIdClient = 0 }, null);
        Assert.Equal((0, 25, 1, -1), (row.CostPixels, row.CostDiamonds, row.ClubLevel, row.OfferId));
        var back = CatalogAdminMapping.ToOffer(row, 77, "NORMAL");
        Assert.Equal((25, 5, true, 77), (back.CostPoints, back.PointsType, back.ClubOnly, back.OfferId));
        var vip = new CatalogOfferRow { ClubLevel = 2 };
        Assert.Equal(2, CatalogAdminMapping.Apply(Offer with { ClubOnly = true }, vip).ClubLevel);
    }
}
