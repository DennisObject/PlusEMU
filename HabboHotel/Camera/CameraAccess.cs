using Plus.Core.Settings;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Camera;

internal static class CameraAccess
{
    internal static bool HasPermission(ISettingsManager settings, Habbo habbo) =>
        settings.TryGetValue("camera.enabled") == "1" && habbo.Access.Can(PermissionKeys.CameraUse);
}
