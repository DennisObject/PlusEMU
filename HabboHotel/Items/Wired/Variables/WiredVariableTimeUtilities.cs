using System.Globalization;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Calendar fields use the room timezone; elapsed units always count from the UTC epoch.</summary>
public sealed record WiredVariableTimeUtilities(int Mask, int Mode)
{
    public const int ValidMask = 0x7FE | 0x7F00000;
    public IEnumerable<int> Selected => Enumerable.Range(1, 26).Where(id => (Mask & (1 << id) & ValidMask) != 0);
    public bool Has(int id) => id is > 0 and <= 26 && (Mask & (1 << id) & ValidMask) != 0;
    public static string Key(int id) => id switch
    {
        1 => "millisecond_of_second",
        2 => "seconds_of_minute",
        3 => "minute_of_hour",
        4 => "hour_of_day",
        5 => "day_of_week",
        6 => "day_of_month",
        7 => "day_of_year",
        8 => "week_of_year",
        9 => "month_of_year",
        10 => "year",
        20 => "millisecond",
        21 => "second",
        22 => "minute",
        23 => "hour",
        24 => "day",
        25 => "week",
        26 => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    public long? Read(WiredVariableValue value, int id, TimeZoneInfo zone)
    {
        if (!Has(id)) {
            return null;
        }

        DateTimeOffset? instant = Mode switch
        {
            1 => value.CreatedAt,
            2 => value.UpdatedAt,
            _ => value.Value <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, value.Value)) : null
        };

        if (instant is null || Mode != 0 && instant.Value.ToUnixTimeMilliseconds() <= 0) {
            return null;
        }

        var utc = instant.Value;
        var milliseconds = utc.ToUnixTimeMilliseconds();
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        long? result = id switch
        {
            1 => local.Millisecond,
            2 => local.Second,
            3 => local.Minute,
            4 => local.Hour,
            5 => local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek,
            6 => local.Day,
            7 => local.DayOfYear,
            8 => ISOWeek.GetWeekOfYear(local.DateTime),
            9 => local.Month,
            10 => local.Year,
            20 => milliseconds,
            21 => milliseconds / 1000,
            22 => milliseconds / 60000,
            23 => milliseconds / 3600000,
            24 => milliseconds / 86400000,
            25 => milliseconds / 604800000,
            26 => (utc.Year - 1970L) * 12 + utc.Month - 1,
            _ => null
        };

        return result;
    }
}

public sealed record WiredVariableDerivation(WiredVariableReference Source, Func<WiredVariableValue, WiredVariableValue?> Convert, bool RequiresValue = true, bool RequiresTimestamps = false);
