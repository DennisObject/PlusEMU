namespace Plus.HabboHotel.Navigator;

public sealed record NavigatorCategoryRow(int Id, string PublicName, bool CanSelect)
{
    // Event categories are always selectable until the navigator gains per-category rules.
    public static NavigatorCategoryRow CaptureEvent(SearchResultList category) => new(category.Id, category.PublicName, true);

    // The caller captures the user's permission keys once for the whole batch, so access cannot change between rows.
    public static NavigatorCategoryRow CaptureForUser(SearchResultList category, IReadOnlySet<string> permissions) =>
        new(category.Id, category.PublicName, category.RequiredPermission.Length == 0 || permissions.Contains(category.RequiredPermission));
}
