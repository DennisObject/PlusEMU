namespace Plus.HabboHotel.Housekeeping;

/// <summary>
/// Server-side bounds for housekeeping input. The client validates too, but nothing it sends is trusted.
/// </summary>
public static class HousekeepingLimits
{
    public const int MaxLookupLength = 64;
    public const int MaxReasonLength = 500;
    public const int MaxAlertLength = 1000;
    public const int MaxBanHours = 24 * 365 * 100;
    public const int MaxMuteMinutes = 60 * 24 * 30;
    public const int MaxTradeLockHours = 24 * 365;
    public const int MaxRoomMuteMinutes = 60 * 24 * 30;
    public const int MaxClubDays = 3650;
    public const int MaxGrantAmount = 1_000_000_000;
    public const int MaxItemQuantity = 100;
    public const int MaxRoomResults = 50;
    public const int MaxActionLogEntries = 500;

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static bool IsText(string value, int maxLength) => value.Length <= maxLength;

    public static bool InRange(int value, int min, int max) => value >= min && value <= max;

    /// <summary>Audit details are single-line and bounded so a reason cannot forge extra log rows.</summary>
    public static string AuditValue(string value)
    {
        var flat = Normalize(value).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return flat.Length > MaxReasonLength ? flat[..MaxReasonLength] : flat;
    }

    /// <summary>Adds a grant to a balance, or returns null when it would overflow the int wire field.</summary>
    public static int? AddToBalance(int balance, int amount)
    {
        var total = (long)balance + amount;
        return total is < 0 or > int.MaxValue ? null : (int)total;
    }

    /// <summary>Unix second at which a duration ends, saturated at the int wire range.</summary>
    public static int UnixUntil(int now, long durationSeconds) => (int)Math.Min(int.MaxValue, now + Math.Max(0, durationSeconds));
}

/// <summary>
/// Rank hierarchy for staff actions: a staff member only acts on strictly lower ranks and only grants ranks below their own.
/// </summary>
public static class HousekeepingRankPolicy
{
    public static bool CanTarget(int operatorRank, int targetRank) => operatorRank > 0 && targetRank > 0 && targetRank < operatorRank;

    public static bool CanAssign(int operatorRank, int newRank) => operatorRank > 0 && newRank > 0 && newRank < operatorRank;

    /// <summary>Rooms owned by a missing account (rank 0) are manageable; otherwise the owner must be lower ranked.</summary>
    public static bool CanManageRoom(int operatorRank, int ownerRank) => operatorRank > 0 && ownerRank < operatorRank;
}
