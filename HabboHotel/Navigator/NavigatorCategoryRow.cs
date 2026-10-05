using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Navigator;

public sealed record NavigatorCategoryRow(int Id, string PublicName, bool CanSelect)
{
    // Event categories are always selectable until the navigator gains per-category rules.
    public static NavigatorCategoryRow CaptureEvent(SearchResultList category) => new(category.Id, category.PublicName, true);

    // The permission is checked once here, so the packet never reads the user's access.
    public static NavigatorCategoryRow CaptureForUser(SearchResultList category, UserAccess access) =>
        new(category.Id, category.PublicName, category.RequiredPermission.Length == 0 || access.Can(category.RequiredPermission));
}
