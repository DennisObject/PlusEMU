namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class Route
{
    private SurfaceRef[] _steps = new SurfaceRef[32];
    // Planned surface Z per step; advisory tie-break data for blocked-route fallback only.
    private double[] _advisoryZ = new double[32];
    public int Count { get; internal set; }
    public int GridVersion { get; internal set; }
    public GraphView View { get; internal set; }
    public SurfaceRef? GoalSurface { get; internal set; }
    public ReadOnlySpan<SurfaceRef> Steps => _steps.AsSpan(0, Count);
    internal void EnsureCapacity(int count)
    {
        if (count <= _steps.Length) return;
        var size = Math.Max(count, _steps.Length * 2);
        Array.Resize(ref _steps, size); Array.Resize(ref _advisoryZ, size);
    }
    internal void Set(int index, SurfaceRef reference) => _steps[index] = reference;
    internal void Clear() { Count = 0; GoalSurface = null; View = GraphView.Surface; }
    internal double AdvisoryZ(int index) => _advisoryZ[index];
    internal void SetAdvisoryZ(int index, double z) => _advisoryZ[index] = z;
    internal void CaptureAdvisory(NavGrid grid)
    {
        for (var i = 0; i < Count; i++) _advisoryZ[i] = grid.Position(_steps[i].Tile, View == GraphView.LegacyTile).Z;
    }
    internal void CopyFrom(Route source)
    {
        EnsureCapacity(source.Count);
        Array.Copy(source._steps, _steps, source.Count); Array.Copy(source._advisoryZ, _advisoryZ, source.Count);
        Count = source.Count; GridVersion = source.GridVersion; View = source.View; GoalSurface = source.GoalSurface;
    }
    // Only a route that still ends at its goal surface has a Goal edge; a truncated prefix is all Transit.
    internal StepPurpose PurposeAt(int index, MoveOrigin origin) => origin == MoveOrigin.Interaction
        ? StepPurpose.Interaction
        : index == Count - 1 && GoalSurface != null ? StepPurpose.Goal : StepPurpose.Transit;
}
