namespace Plus.HabboHotel.Navigator;

internal static class RoomCategoryChoice
{
    public const int Fallback = 36;

    public static int Resolve(int requested, SearchResultList? list, int userRank, int userId, int ownerId, bool applyOwnerRule)
    {
        if (list == null || list.CategoryType != NavigatorCategoryType.Category || list.RequiredRank > userRank)
            return Fallback;
        if (applyOwnerRule && userId != ownerId && userRank >= list.RequiredRank)
            return Fallback;
        return requested;
    }
}
