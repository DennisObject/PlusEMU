using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Navigator;

internal static class RoomCategoryChoice
{
    public const int Fallback = 36;

    public static int Resolve(int requested, SearchResultList? list, UserAccess access, int userId, int ownerId, bool applyOwnerRule)
    {
        if (list == null || list.CategoryType != NavigatorCategoryType.Category || list.RequiredPermission.Length > 0 && !access.Can(list.RequiredPermission))
        {
            return Fallback;
        }

        if (applyOwnerRule && userId != ownerId)
        {
            return Fallback;
        }

        return requested;
    }
}
