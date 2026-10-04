using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms;

internal static class RoomModerationPolicy
{
    public static bool CanTarget(UserAccess actor, UserAccess target) =>
        !(actor.Can(PermissionKeys.RoomOwnerAny) || target.Can(PermissionKeys.RoomOwnerAny) || target.Can(PermissionKeys.ModerationTool)) || actor.Outranks(target);
}
