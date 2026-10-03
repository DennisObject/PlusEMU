namespace Plus.HabboHotel.Rooms.PathFinding;

public static class PathTieBreak
{
    private static readonly (int X, int Y)[] Offsets = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];
    public static ReadOnlySpan<(int X, int Y)> Neighbours => Offsets;
    public static int Heuristic(int x, int y, int goalX, int goalY) => Math.Max(Math.Abs(x - goalX), Math.Abs(y - goalY));
    public static ulong Key(int f, int x, int y, int goalX, int goalY, byte ordinal, int sequence)
    {
        var dx = x - goalX; var dy = y - goalY; var tie = dx * dx + dy * dy;
        System.Diagnostics.Debug.Assert((uint)f < 1 << 20 && (uint)tie < 1 << 20 && ordinal < 4 && (uint)sequence < 1 << 22);
        return (ulong)f << 44 | (ulong)tie << 24 | (ulong)ordinal << 22 | (ulong)sequence;
    }
}
