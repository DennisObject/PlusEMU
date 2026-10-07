namespace Plus.HabboHotel.Catalog.Admin;

// Converts between catalog rows and the editor's snapshots.
// The editor's page images and texts are slots of catalog_page_images and catalog_page_texts;
// slots the editor does not know are kept as they are.
public static class CatalogAdminMapping
{
    public const int DiamondsPointsType = 5;
    public const int DucketsPointsType = 0;

    public static CatalogAdminPage ToPage(CatalogPageRow row)
    {
        var images = row.Images;
        var texts = row.Texts;
        const string type = CatalogAdminTypes.Normal;

        return new(type, row.Id, row.ParentId, row.PageLink, row.Caption, row.PageLayout, 1, row.IconImage, row.RequiredPermission ?? string.Empty,
            row.OrderNum, row.Visible, row.Enabled, row.RequiredClubLevel > 0, type,
            At(images, 0), At(images, 1), At(images, 2), At(texts, 0), At(texts, 1), At(texts, 2), At(texts, 3), 0, string.Empty);
    }

    public static CatalogPageRow Apply(CatalogAdminPage page, CatalogPageRow? existing)
    {
        var row = existing?.Copy() ?? new CatalogPageRow();
        row.ParentId = page.ParentId;
        row.Caption = page.Caption;
        row.PageLink = page.CaptionSave;
        row.IconImage = page.IconImage;
        row.Visible = page.Visible;
        row.Enabled = page.Enabled;
        row.RequiredClubLevel = page.ClubOnly ? 2 : 0;
        row.RequiredPermission = string.IsNullOrEmpty(page.RequiredPermission) ? null : page.RequiredPermission;
        row.PageLayout = page.PageLayout;
        row.Images = SetStrings(row.Images, page.PageHeadline, page.PageTeaser, page.PageSpecial);
        row.Texts = SetStrings(row.Texts, page.PageText1, page.PageText2, page.PageTextDetails, page.PageTextTeaser);

        if (page.OrderNum >= 0) {
            row.OrderNum = page.OrderNum;
        }

        return row;
    }

    public static CatalogPageRow WithImages(CatalogPageRow row, string headerImage, string teaserImage)
    {
        var copy = row.Copy();
        copy.Images = SetStrings(row.Images, headerImage, teaserImage, At(row.Images, 2));

        return copy;
    }

    public static CatalogAdminOffer ToOffer(CatalogOfferRow row, int offerId, string catalogType)
    {
        bool diamonds = row.CostDiamonds > 0;

        return new(catalogType, offerId, row.ItemId, row.PageId, row.CatalogName, row.CostCredits,
            diamonds ? row.CostDiamonds : row.CostPixels, diamonds ? DiamondsPointsType : DucketsPointsType, row.Amount,
            row.LimitedStack, row.OrderNum, row.OfficialOfferId, 0, row.Extradata, row.HabbiconId > 0 ? row.Enabled : row.BulkPurchase, row.ClubLevel > 0)
        {
            LimitedSells = row.LimitedSells
        };
    }

    public static CatalogOfferRow Apply(CatalogAdminOffer offer, CatalogOfferRow? existing)
    {
        var row = existing?.Copy() ?? new CatalogOfferRow();
        row.PageId = offer.PageId;

        if (row.HasEditableItem || existing == null) {
            row.ItemId = offer.ItemIds.Trim();
            row.Amount = offer.Amount;
            row.Extradata = offer.Extradata;
        }

        row.CatalogName = offer.CatalogName;
        row.CostCredits = offer.CostCredits;
        row.CostPixels = offer.PointsType == DucketsPointsType ? offer.CostPoints : 0;
        row.CostDiamonds = offer.PointsType == DiamondsPointsType ? offer.CostPoints : 0;
        row.LimitedStack = offer.LimitedStack;

        // Habbicon offers used this flag for whether the offer is on sale; other offers for buying several at once.
        if (row.HabbiconId > 0) {
            row.Enabled = offer.HaveOffer;
        }
        else {
            row.BulkPurchase = offer.HaveOffer;
            row.Enabled = true;
        }

        row.ClubLevel = offer.ClubOnly ? Math.Max(row.ClubLevel, 1) : 0;

        if (offer.OrderNumber >= 0) {
            row.OrderNum = offer.OrderNumber;
        }

        return row;
    }

    private static string At(List<string> values, int index) => index < values.Count ? values[index] : string.Empty;

    // Writes values from slot 0 and drops trailing empty slots the original did not have.
    internal static List<string> SetStrings(List<string> original, params string[] values)
    {
        var list = new List<string>(original);
        int originalCount = list.Count;

        for (int i = 0; i < values.Length; i++) {
            while (list.Count <= i) {
                list.Add(string.Empty);
            }

            list[i] = values[i];
        }

        while (list.Count > originalCount && list[^1].Length == 0) {
            list.RemoveAt(list.Count - 1);
        }

        return list;
    }
}
