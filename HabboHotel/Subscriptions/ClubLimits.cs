using Plus.Core.Settings;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Subscriptions;

internal static class ClubLimits
{
    public static int For(UserAccess access, string kind, ISettingsManager settings)
    {
        var snapshot = access.Capture(out var now);
        var member = ClubAccess.LevelFor(snapshot, now) > 0;
        var fallback = kind switch
        {
            "rooms" => member ? 100 : 50,
            "friends" => member ? 800 : 300,
            "visitors" => member ? 75 : 50,
            _ => throw new ArgumentException(nameof(kind))
        };
        var configured = settings.GetOptionalValue($"club.limit.{kind}.{(member ? "member" : "normal")}");

        if (int.TryParse(configured, out var value) && value >= 0)
        {
            fallback = value;
        }

        if (kind == "visitors" && snapshot.Keys.Contains(PermissionKeys.RoomUserLimitOverride))
        {
            fallback = int.MaxValue;
        }

        return Math.Max(0, snapshot.Limits.GetValueOrDefault($"limit.{kind}", fallback));
    }
}
