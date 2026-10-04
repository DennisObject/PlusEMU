namespace Plus.HabboHotel.Permissions;

internal static class AccessLimits
{
    public static readonly IReadOnlyDictionary<string, int> Defaults = new Dictionary<string, int>
    {
        ["limit.daily_respects"] = 10, ["limit.daily_pet_respects"] = 10,
        ["limit.name_change_frequency"] = 0, ["limit.currency_credits"] = 0,
        ["limit.currency_duckets"] = 0, ["limit.flood_tolerance"] = 1, ["limit.staff_effect"] = 0
    };
}
