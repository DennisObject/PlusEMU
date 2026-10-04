using Plus.Utilities;

namespace Plus.HabboHotel.Moderation;

/// <summary>
/// The one clock for ban expiry. PlusEMU has always written and checked bans.expire as Unix seconds on the local wall
/// clock (UnixTimestamp.GetNow); every ban producer and every expiry check uses this instead of mixing in UTC.
/// </summary>
public static class BanClock
{
    public static double Now() => UnixTimestamp.GetNow();

    /// <summary>The same clock for code that runs on a <see cref="TimeProvider"/>.</summary>
    public static double Now(TimeProvider time)
    {
        var utc = time.GetUtcNow();
        return utc.ToUnixTimeMilliseconds() / 1000.0 + time.LocalTimeZone.GetUtcOffset(utc).TotalSeconds;
    }
}
