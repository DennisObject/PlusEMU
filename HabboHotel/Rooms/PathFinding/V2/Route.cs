namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class Route
{
    private SurfaceRef[] _steps = new SurfaceRef[32];
    public int Count { get; internal set; }
    public int GridVersion { get; internal set; }
    public SurfaceRef? GoalSurface { get; internal set; }
    public ReadOnlySpan<SurfaceRef> Steps => _steps.AsSpan(0, Count);
    internal void EnsureCapacity(int count)
    {
        if (count > _steps.Length) Array.Resize(ref _steps, Math.Max(count, _steps.Length * 2));
    }
    internal void Set(int index, SurfaceRef reference) => _steps[index] = reference;
    internal void Clear() { Count = 0; GoalSurface = null; }
}
