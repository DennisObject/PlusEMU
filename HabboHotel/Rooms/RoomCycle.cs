namespace Plus.HabboHotel.Rooms;

internal static class RoomCycle
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan WiredInterval = TimeSpan.FromMilliseconds(50);
    public const int MissesUntilCrash = 30;

    public readonly record struct RoomTick(bool Start, int Lag, bool Crashed);

    public static bool IsDue(DateTimeOffset lastRun, DateTimeOffset now) =>
        now - lastRun >= Interval;

    // Both passes share the room's task slot. Fast misses do not change legacy lag counters.
    public static bool TryStart(Room room, Action pass)
    {
        if (room.IsCrashed || room.MDisposed || room.ProcessTask is { IsCompleted: false })
        {
            return false;
        }

        room.ProcessTask?.Dispose();
        room.ProcessTask = Task.Run(pass);

        return true;
    }

    public static RoomTick Next(bool workInProgress, int lag)
    {
        if (!workInProgress)
        {
            return new(true, 0, false);
        }

        var nextLag = lag + 1;

        return new(false, nextLag, nextLag >= MissesUntilCrash);
    }
}
