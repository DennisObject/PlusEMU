using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Subscriptions;

internal static class ClubAccess
{
    // This hotel's club access is free; purchased time only controls the displayed day counters.
    public static int LevelFor(UserAccess access) => 2;
}
