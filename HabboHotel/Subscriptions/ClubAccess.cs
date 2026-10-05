using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Subscriptions;

internal static class ClubAccess
{
    public static int LevelFor(UserAccess access)
    {
        var snapshot = access.Capture(out var now);
        return LevelFor(snapshot, now.ToUnixTimeSeconds());
    }
    internal static int LevelFor(UserAccess.Snapshot snapshot, long now) =>
        snapshot.Keys.Contains(PermissionKeys.ClubAccess) || snapshot.Membership.Active(now) ? 2 : 0;
}
