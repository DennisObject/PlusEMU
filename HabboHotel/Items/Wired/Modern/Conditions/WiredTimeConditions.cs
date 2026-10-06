using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Conditions;

/// <summary>The legacy calendar editor stores skip/exact/range modes, not Turbo use flags.</summary>
public static class WiredTimeConditions
{
    public static bool MatchesTime(WiredConfiguration config, DateTimeOffset roomLocalTime) =>
        MatchesPart(roomLocalTime.Hour, Param(config, 0), Param(config, 1), Param(config, 2))
        && MatchesPart(roomLocalTime.Minute, Param(config, 3), Param(config, 4), Param(config, 5))
        && MatchesPart(roomLocalTime.Second, Param(config, 6), Param(config, 7), Param(config, 8));

    public static bool MatchesDate(WiredConfiguration config, DateTimeOffset roomLocalTime)
    {
        var weekdayMask = Param(config, 0, 127);
        var monthMask = Param(config, 4, 4095);
        // Octane weekdays are Monday=0 through Sunday=6.
        var weekday = ((int)roomLocalTime.DayOfWeek + 6) % 7;

        return (weekdayMask == 0 || (weekdayMask & (1 << weekday)) != 0)
            && (monthMask == 0 || (monthMask & (1 << (roomLocalTime.Month - 1))) != 0)
            && MatchesPart(roomLocalTime.Day, Param(config, 1), Param(config, 2, 1), Param(config, 3, 31))
            && MatchesPart(roomLocalTime.Year, Param(config, 5), Param(config, 6), Param(config, 7, 9999));
    }

    public static bool MatchesRange(WiredConfiguration config, DateTimeOffset now)
    {
        var start = Param(config, 0);
        var end = Param(config, 1);
        var seconds = now.ToUnixTimeSeconds();

        return (start == 0 || seconds >= start) && (end == 0 || seconds <= end);
    }

    public static bool MatchesElapsed(WiredConfiguration config, long elapsedMs, bool moreThan)
    {
        var targetMs = Param(config, 0, 1) * 500L;

        return moreThan ? elapsedMs > targetMs : elapsedMs < targetMs;
    }

    public static bool MatchesCounter(WiredConfiguration config, IEnumerable<long> currentTimesMs)
    {
        var targetMs = Param(config, 1) * 60_000L + Param(config, 2) * 500L;

        return WiredRoomOperations.Quantify(currentTimesMs.Select(value =>
            WiredRoomOperations.Compare(value, targetMs, Param(config, 0, 1))), Param(config, 4));
    }

    public static bool MatchesPart(int value, int mode, int from, int to) => mode switch
    {
        0 => true,
        1 => value == from,
        2 => from <= to ? value >= from && value <= to : value >= from || value <= to,
        _ => false
    };

    private static int Param(WiredConfiguration config, int index, int fallback = 0) =>
        index < config.IntParams.Length ? config.IntParams[index] : fallback;
}
