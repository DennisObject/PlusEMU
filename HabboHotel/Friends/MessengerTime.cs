using Plus.Database;

namespace Plus.HabboHotel.Friends;

// Messenger times are stored as UTC DATETIME(6), with NULL for unknown times. Clients receive whole Unix seconds clamped into an int.
internal static class MessengerTime
{
    // A missing time has no wire value, so it reads as 0. Pre-1970 values read as 0 and values past 2038 read as int.MaxValue.
    public static int WireSeconds(DateTimeOffset? utc) => utc is { } value ? ClampSeconds((value - DateTimeOffset.UnixEpoch).TotalSeconds) : 0;

    // A missing time reads as 0 seconds ago, and a stored time in the future reads as 0 too, so clock skew never shows a negative age.
    public static int SecondsBetween(DateTimeOffset now, DateTimeOffset? sentAt) => sentAt is { } value ? ClampSeconds((now - value).TotalSeconds) : 0;

    // Typed tuple members bypass type handlers, so native DATETIME values are converted here through the shared UTC handler.
    public static DateTimeOffset? ReadUtc(object? value) => value is null or DBNull ? null : new UtcDateTimeOffsetHandler().Parse(value);

    private static int ClampSeconds(double seconds) => seconds <= 0 ? 0 : seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
}
