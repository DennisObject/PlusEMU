using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms;

internal static class RoomModerationPolicy
{
    public static bool CanTarget(UserAccess actor, UserAccess target) =>
        !(actor.Can(PermissionKeys.RoomOwnerAny) || target.Roles.Any(role => role.IsStaff)) || actor.Outranks(target);
}
