using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Catalog.Admin;

// Server-side checks for editor input. Errors are keyed by the editor's field names; "_form" is not tied to a field.
public static partial class CatalogAdminValidation
{
    public const string Form = "_form";
    public const int RootParentId = -1;
    private const int MaxImageLength = 255;
    private const int MaxTextLength = 8192;
    private const int MaxPrice = 1_000_000;
    private const int MaxDepth = 64;
    private const string Unsupported = "Not supported by this hotel.";

    [GeneratedRegex("^[A-Za-z0-9_.-]{0,128}$")]
    private static partial Regex PageLinkPattern();

    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex LayoutPattern();

    public static Dictionary<string, string> Page(CatalogAdminPage page, CatalogPageRow? existing, int actorRank, Func<int, CatalogPageRow?> findPage)
    {
        var errors = new Dictionary<string, string>();
        if (existing != null && existing.MinRank > actorRank)
            errors[Form] = "You cannot edit a page above your rank.";
        Text(errors, "caption", page.Caption, 128, required: true);
        if (!PageLinkPattern().IsMatch(page.CaptionSave ?? string.Empty))
            errors["captionSave"] = "Use up to 128 letters, digits, '_', '-' or '.'.";
        if (!LayoutPattern().IsMatch(page.PageLayout ?? string.Empty))
            errors["pageLayout"] = "Unknown layout.";
        if (page.IconImage is < 0 or > 1_000_000)
            errors["iconImage"] = "Icon must be between 0 and 1000000.";
        if (page.MinRank < 1 || page.MinRank > actorRank)
            errors["minRank"] = $"Minimum rank must be between 1 and your rank ({actorRank}).";
        if (page.OrderNum < -1)
            errors["orderNum"] = "Order cannot be negative.";
        var type = CatalogAdminTypes.Parse(page.CatalogMode);
        if (type == null)
            errors["catalogMode"] = "Pages belong to the normal or the builders club catalog.";
        else if (existing != null && CatalogAdminTypes.FromMode(existing.CatalogMode) != type)
            errors["catalogMode"] = "A page cannot move to the other catalog.";
        Parent(errors, page.PageId, page.ParentId, type, findPage);
        PageString(errors, "pageHeadline", page.PageHeadline, MaxImageLength);
        PageString(errors, "pageTeaser", page.PageTeaser, MaxImageLength);
        PageString(errors, "pageSpecial", page.PageSpecial, MaxImageLength);
        PageString(errors, "pageText1", page.PageText1, MaxTextLength);
        PageString(errors, "pageText2", page.PageText2, MaxTextLength);
        PageString(errors, "pageTextDetails", page.PageTextDetails, MaxTextLength);
        PageString(errors, "pageTextTeaser", page.PageTextTeaser, MaxTextLength);
        if (page.ClubOnly)
            errors["clubOnly"] = Unsupported;
        if (page.RoomId != 0)
            errors["roomId"] = Unsupported;
        if (!string.IsNullOrEmpty(page.Includes))
            errors["includes"] = Unsupported;
        return errors;
    }

    public static Dictionary<string, string> Offer(CatalogAdminOffer offer, CatalogOfferRow? existing, int actorRank,
        Func<int, CatalogPageRow?> findPage, Func<uint, bool> itemExists)
    {
        var errors = new Dictionary<string, string>();
        var page = findPage(offer.PageId);
        if (page == null)
            errors["pageId"] = "Page not found.";
        else if (page.MinRank > actorRank)
            errors["pageId"] = "You cannot edit a page above your rank.";
        if (existing != null && findPage(existing.PageId) is { } current && current.MinRank > actorRank)
            errors[Form] = "You cannot edit an offer on a page above your rank.";
        ItemIds(errors, offer, existing, itemExists);
        Text(errors, "catalogName", offer.CatalogName, 100, required: true);
        if (offer.CostCredits is < 0 or > MaxPrice)
            errors["costCredits"] = $"Credits must be between 0 and {MaxPrice}.";
        if (offer.CostPoints is < 0 or > MaxPrice)
            errors["costPoints"] = $"Points must be between 0 and {MaxPrice}.";
        if (offer.PointsType is not (CatalogAdminMapping.DucketsPointsType or CatalogAdminMapping.DiamondsPointsType))
            errors["pointsType"] = "Points are duckets (0) or diamonds (5).";
        if (offer.Amount is < 1 or > 100)
            errors["amount"] = "Amount must be between 1 and 100.";
        if (offer.Extradata.Length > 1024)
            errors["extradata"] = "Extra data is limited to 1024 characters.";
        if (offer.OfferIdClient < -1)
            errors["offerIdGroup"] = "Offer id cannot be negative.";
        if (offer.LimitedStack is < 0 or > MaxPrice)
            errors["limitedStack"] = $"Limited stack must be between 0 and {MaxPrice}.";
        else if (existing != null && offer.LimitedStack < existing.LimitedSells && offer.LimitedStack != existing.LimitedStack)
            errors["limitedStack"] = $"{existing.LimitedSells} are already sold.";
        if (offer.OrderNumber < -1)
            errors["orderNumber"] = "Order cannot be negative.";
        if (offer.SongId != 0)
            errors["songId"] = Unsupported;
        return errors;
    }

    public static string? Summary(Dictionary<string, string> errors) =>
        errors.Count == 0 ? null : errors.TryGetValue(Form, out var form) ? form : $"{errors.First().Key}: {errors.First().Value}";

    public static Dictionary<string, string> Move(int pageId, int parentId, string catalogType, Func<int, CatalogPageRow?> findPage)
    {
        var errors = new Dictionary<string, string>();
        Parent(errors, pageId, parentId, catalogType, findPage);
        return errors;
    }

    public static Dictionary<string, string> PageImages(string headerImage, string teaserImage)
    {
        var errors = new Dictionary<string, string>();
        PageString(errors, "pageHeadline", headerImage, MaxImageLength);
        PageString(errors, "pageTeaser", teaserImage, MaxImageLength);
        return errors;
    }

    // Walks up from the new parent; reaching the page itself would make it its own ancestor.
    private static void Parent(Dictionary<string, string> errors, int pageId, int parentId, string? type, Func<int, CatalogPageRow?> findPage)
    {
        if (parentId == RootParentId)
            return;
        var parent = findPage(parentId);
        if (parent == null)
        {
            errors["parentId"] = "Parent page not found.";
            return;
        }
        if (type != null && CatalogAdminTypes.FromMode(parent.CatalogMode) != type)
            errors["parentId"] = "The parent page is in the other catalog.";
        for (int depth = 0; parent != null && depth < MaxDepth; depth++)
        {
            if (pageId > 0 && parent.Id == pageId)
            {
                errors["parentId"] = "A page cannot be moved under itself.";
                return;
            }
            parent = parent.ParentId == RootParentId ? null : findPage(parent.ParentId);
        }
        if (parent != null)
            errors["parentId"] = "The page tree is too deep.";
    }

    private static void ItemIds(Dictionary<string, string> errors, CatalogAdminOffer offer, CatalogOfferRow? existing, Func<uint, bool> itemExists)
    {
        var itemIds = offer.ItemIds.Trim();
        // Habbicon offers sell an icon, not furniture; their item id is kept as it is.
        if (existing is { HabbiconId: > 0 })
        {
            if (itemIds != existing.ItemId)
                errors["itemIds"] = "The item of a habbicon offer cannot change.";
            return;
        }
        if (!uint.TryParse(itemIds, out var itemId) || itemId == 0)
            errors["itemIds"] = "Enter one furniture id; bundles are not supported by this hotel.";
        else if (!itemExists(itemId))
            errors["itemIds"] = $"Furniture #{itemId} does not exist.";
    }

    private static void PageString(Dictionary<string, string> errors, string field, string? value, int maxLength)
    {
        if (value == null)
            return;
        if (value.Contains('|'))
            errors[field] = "'|' separates page texts and cannot be used.";
        else if (value.Length > maxLength)
            errors[field] = $"Limited to {maxLength} characters.";
    }

    private static void Text(Dictionary<string, string> errors, string field, string? value, int maxLength, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                errors[field] = "Required.";
            return;
        }
        if (value.Length > maxLength)
            errors[field] = $"Limited to {maxLength} characters.";
        else if (value.Any(char.IsControl))
            errors[field] = "Control characters are not allowed.";
    }
}
