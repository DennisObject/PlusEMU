namespace Plus.HabboHotel.Rooms;

internal static class RoomCycle
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);
    public const int MissesUntilCrash = 30;

    public readonly record struct RoomTick(bool Start, int Lag, bool Crashed);

    public static bool IsDue(DateTimeOffset lastRun, DateTimeOffset now) =>
        now - lastRun >= Interval;

    public static RoomTick Next(bool workInProgress, int lag)
    {
        if (!workInProgress)
            return new(true, 0, false);

        var nextLag = lag + 1;
        return new(false, nextLag, nextLag >= MissesUntilCrash);
    }
}
