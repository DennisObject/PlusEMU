using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.Permissions;
using Plus.Core.Settings;

namespace Plus.HabboHotel.Users.Permissions;

/// <summary>
/// The rank rights mirrored to the client. The client only uses them to show staff tools;
/// every action is still authorized on the server.
/// </summary>
internal static class ClientPermissions
{
    internal static readonly string[] Exposed = { "acc_housekeeping", "acc_soundboard_manage", "acc_catalogfurni" };

    internal static IReadOnlyList<string> Resolve(PermissionComponent permissions, bool hasCamera)
    {
        var resolved = new List<string>();
        // Camera access follows the camera settings, not a rank right.
        if (hasCamera) resolved.Add("acc_camera");
        resolved.AddRange(Exposed.Where(permissions.HasRight));
        return resolved;
    }

    internal static UserRightsComposer Composer(Habbo habbo, IPermissionManager permissionManager, ISettingsManager settings)
    {
        permissionManager.TryGetGroup(habbo.Rank, out var group);
        return new(habbo.Rank, habbo.IsAmbassador, group?.Name ?? string.Empty, group?.Badge ?? string.Empty,
            Resolve(habbo.Permissions, CameraAccess.HasPermission(settings, habbo)));
    }
}
