using System.Text.RegularExpressions;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

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

    [GeneratedRegex("^[a-z0-9_]+(?:\\.[a-z0-9_]+)*$")]
    private static partial Regex PermissionPattern();

    public static bool Available(string? permission, UserAccess access) =>
        string.IsNullOrEmpty(permission) || access.Can(permission);

    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex LayoutPattern();

    public static Dictionary<string, string> Page(CatalogAdminPage page, CatalogPageRow? existing, UserAccess access, Func<int, CatalogPageRow?> findPage)
    {
        var errors = new Dictionary<string, string>();

        if (existing != null && (!Available(existing.RequiredPermission, access) || Plus.HabboHotel.Subscriptions.ClubAccess.LevelFor(access) < existing.RequiredClubLevel)) {
            errors[Form] = "You cannot edit a page requiring a permission you do not have.";
        }

        Text(errors, "caption", page.Caption, 128, required: true);

        if (!PageLinkPattern().IsMatch(page.CaptionSave ?? string.Empty)) {
            errors["captionSave"] = "Use up to 128 letters, digits, '_', '-' or '.'.";
        }

        if (!LayoutPattern().IsMatch(page.PageLayout ?? string.Empty)) {
            errors["pageLayout"] = "Unknown layout.";
        }

        if (page.IconImage is < 0 or > 1_000_000) {
            errors["iconImage"] = "Icon must be between 0 and 1000000.";
        }

        if (page.RequiredPermission == null || page.RequiredPermission.Length > 128 || (page.RequiredPermission.Length > 0 &&
            (!PermissionPattern().IsMatch(page.RequiredPermission) || !access.Can(page.RequiredPermission)))) {
            errors["requiredPermission"] = "Choose a permission you have, or leave empty for everyone.";
        }

        if (page.OrderNum < -1) {
            errors["orderNum"] = "Order cannot be negative.";
        }

        var type = CatalogAdminTypes.Parse(page.CatalogMode);

        if (type == null) {
            errors["catalogMode"] = "Only the normal catalog is supported.";
        }

        Parent(errors, page.PageId, page.ParentId, access, findPage);
        PageString(errors, "pageHeadline", page.PageHeadline, MaxImageLength);
        PageString(errors, "pageTeaser", page.PageTeaser, MaxImageLength);
        PageString(errors, "pageSpecial", page.PageSpecial, MaxImageLength);
        PageString(errors, "pageText1", page.PageText1, MaxTextLength);
        PageString(errors, "pageText2", page.PageText2, MaxTextLength);
        PageString(errors, "pageTextDetails", page.PageTextDetails, MaxTextLength);
        PageString(errors, "pageTextTeaser", page.PageTextTeaser, MaxTextLength);

        if (page.ClubOnly && Plus.HabboHotel.Subscriptions.ClubAccess.LevelFor(access) == 0) {
            errors["clubOnly"] = "Habbo Club is required to create club pages.";
        }

        if (page.RoomId != 0) {
            errors["roomId"] = Unsupported;
        }

        if (!string.IsNullOrEmpty(page.Includes)) {
            errors["includes"] = Unsupported;
        }

        return errors;
    }

    public static Dictionary<string, string> Offer(CatalogAdminOffer offer, CatalogOfferRow? existing, UserAccess access,
        Func<int, CatalogPageRow?> findPage, Func<uint, bool> itemExists)
    {
        var errors = new Dictionary<string, string>();
        var page = findPage(offer.PageId);

        if (page == null) {
            errors["pageId"] = "Page not found.";
        }
        else if (!Available(page.RequiredPermission, access)) {
            errors["pageId"] = "You cannot edit a page requiring a permission you do not have.";
        }

        if (existing != null && findPage(existing.PageId) is { } current && !Available(current.RequiredPermission, access)) {
            errors[Form] = "You cannot edit an offer on a page requiring a permission you do not have.";
        }

        ItemIds(errors, offer, existing, itemExists);
        Text(errors, "catalogName", offer.CatalogName, 100, required: true);

        if (offer.CostCredits is < 0 or > MaxPrice) {
            errors["costCredits"] = $"Credits must be between 0 and {MaxPrice}.";
        }

        if (offer.CostPoints is < 0 or > MaxPrice) {
            errors["costPoints"] = $"Points must be between 0 and {MaxPrice}.";
        }

        if (!ActivityPointType.IsValid(offer.PointsType)) {
            errors["pointsType"] = "Points type must be 0 (duckets), 5 (diamonds) or another activity point type.";
        }

        if (offer.Amount is < 1 or > 100) {
            errors["amount"] = "Amount must be between 1 and 100.";
        }

        if (offer.Extradata.Length > 1024) {
            errors["extradata"] = "Extra data is limited to 1024 characters.";
        }

        if (offer.OfferIdClient is < -1 or >= CatalogOfferIndex.CustomOfferIdBase) {
            errors["offerIdGroup"] = $"An official offer id is between 1 and {CatalogOfferIndex.CustomOfferIdBase - 1}, or -1 for none.";
        }

        if (offer.LimitedStack is < 0 or > MaxPrice) {
            errors["limitedStack"] = $"Limited stack must be between 0 and {MaxPrice}.";
        }
        else if (existing != null && offer.LimitedStack < existing.LimitedSells && offer.LimitedStack != existing.LimitedStack) {
            errors["limitedStack"] = $"{existing.LimitedSells} are already sold.";
        }

        if (offer.OrderNumber < -1) {
            errors["orderNumber"] = "Order cannot be negative.";
        }

        if (offer.SongId != 0) {
            errors["songId"] = Unsupported;
        }

        return errors;
    }

    public static string? Summary(Dictionary<string, string> errors) =>
        errors.Count == 0 ? null : errors.TryGetValue(Form, out var form) ? form : $"{errors.First().Key}: {errors.First().Value}";

    public static Dictionary<string, string> Move(int pageId, int parentId, string catalogType, UserAccess access, Func<int, CatalogPageRow?> findPage)
    {
        var errors = new Dictionary<string, string>();

        if (CatalogAdminTypes.Parse(catalogType) == null) {
            errors["catalogMode"] = "Only the normal catalog is supported.";
        }

        Parent(errors, pageId, parentId, access, findPage);

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
    private static void Parent(Dictionary<string, string> errors, int pageId, int parentId, UserAccess access, Func<int, CatalogPageRow?> findPage)
    {
        if (parentId == RootParentId) {
            return;
        }

        var parent = findPage(parentId);

        if (parent == null) {
            errors["parentId"] = "Parent page not found.";

            return;
        }

        if (!Available(parent.RequiredPermission, access)) {
            errors["parentId"] = "You cannot use a page requiring a permission you do not have as parent.";

            return;
        }

        for (int depth = 0; parent != null && depth < MaxDepth; depth++) {
            if (pageId > 0 && parent.Id == pageId) {
                errors["parentId"] = "A page cannot be moved under itself.";

                return;
            }

            parent = parent.ParentId == RootParentId ? null : findPage(parent.ParentId);
        }

        if (parent != null) {
            errors["parentId"] = "The page tree is too deep.";
        }
    }

    private static void ItemIds(Dictionary<string, string> errors, CatalogAdminOffer offer, CatalogOfferRow? existing, Func<uint, bool> itemExists)
    {
        var itemIds = offer.ItemIds.Trim();

        // Habbicons, effects, badges, bots, pets and bundles are not one piece of furniture; what they sell is kept as it is.
        if (existing is { HasEditableItem: false }) {
            if (itemIds != existing.ItemId) {
                errors["itemIds"] = "What this offer sells cannot change in the editor.";
            }

            return;
        }

        if (!uint.TryParse(itemIds, out var itemId) || itemId == 0) {
            errors["itemIds"] = "Enter one furniture id; bundles are not supported by this hotel.";
        }
        else if (!itemExists(itemId)) {
            errors["itemIds"] = $"Furniture #{itemId} does not exist.";
        }
    }

    private static void PageString(Dictionary<string, string> errors, string field, string? value, int maxLength)
    {
        if (value == null) {
            return;
        }

        if (value.Length > maxLength) {
            errors[field] = $"Limited to {maxLength} characters.";
        }
    }

    private static void Text(Dictionary<string, string> errors, string field, string? value, int maxLength, bool required)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            if (required) {
                errors[field] = "Required.";
            }

            return;
        }

        if (value.Length > maxLength) {
            errors[field] = $"Limited to {maxLength} characters.";
        }
        else if (value.Any(char.IsControl)) {
            errors[field] = "Control characters are not allowed.";
        }
    }
}
