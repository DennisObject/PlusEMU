using System.Collections.Immutable;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Permissions;

public sealed record UserRightsSnapshot(int ClubLevel, int SecurityLevel, bool Ambassador,
    int PrimaryRoleId, string PrimaryRoleName, string PrimaryRoleBadge, ImmutableArray<string> Keys)
{
    public static UserRightsSnapshot Capture(UserAccess access)
    {
        var resolved = access.Capture(out var now);
        var primary = resolved.PrimaryRole;
        return new(ClubAccess.LevelFor(resolved, now), resolved.SecurityLevel,
            resolved.Keys.Contains(PermissionKeys.Ambassador), primary?.Id ?? 0,
            primary?.Name ?? string.Empty, primary?.BadgeCode ?? string.Empty,
            resolved.Keys.Order(StringComparer.Ordinal).ToImmutableArray());
    }
}
