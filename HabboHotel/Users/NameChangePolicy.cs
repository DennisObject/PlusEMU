namespace Plus.HabboHotel.Users;

internal static class NameChangePolicy
{
    public static bool CanChange(Habbo habbo, double now)
    {
        var frequency = habbo.Access.Limit("limit.name_change_frequency", 0);
        return habbo.LastNameChangedAt == null || frequency > 0 && now - habbo.LastNameChangedAt.Value.ToUnixTimeSeconds() >= 604800.0 / frequency;
    }
}
