using Plus.Core.Settings;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Camera;

internal static class CameraAccess
{
    internal static bool HasPermission(ISettingsManager settings, Habbo habbo)
    {
        if (settings.TryGetValue("camera.enabled") != "1") return false;
        var permission = settings.TryGetValue("camera.permission");
        return permission == "0" || habbo.Permissions.HasRight(permission);
    }
}
