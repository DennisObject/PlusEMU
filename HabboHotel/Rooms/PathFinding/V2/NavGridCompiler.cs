using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class NavGridCompiler(NavGrid grid, NavInputs inputs, PathfindingSettings settings)
{
    private readonly LayeredTileCompiler _layers = new(grid, settings);
    internal Action<IReadOnlySet<int>>? BeforePublish { get; set; }
    // Surfaces holding members or claims; the overflow cap keeps them first (§5.3 step 5).
    internal Func<SurfaceRef, bool>? SurfacePinned { get => _layers.SurfacePinned; set => _layers.SurfacePinned = value; }
    public void ApplyNow() => Apply();
    public void RebuildAll() { inputs.MarkAllDirty(); Apply(); }

    internal void Apply(Action? afterDrain = null, Action<uint>? beforeRead = null)
    {
        var tiles = inputs.Drain();
        afterDrain?.Invoke();
        if (tiles.Count == 0) return;
        var selected = SelectClosure(tiles, beforeRead);
        var records = new Dictionary<uint, NavItemRecord>(inputs.AppliedRecords);
        foreach (var (id, record) in selected) records[id] = record;
        grid.BeginPublish();
        SwitchMode(records.Values, tiles);
        var covering = Covering(records.Values, tiles);
        var compat = new Dictionary<int, CompatSurface>(tiles.Count);
        foreach (var t in tiles) compat[t] = CompileCompat(t, covering.GetValueOrDefault(t));
        if (grid.Layered) _layers.Compile(compat, covering);
        else grid.SettleLeftPrimaries();
        foreach (var (id, record) in selected) inputs.AppliedRecords[id] = record;
        BeforePublish?.Invoke(tiles);
        grid.Version++;
    }

    private Dictionary<uint, NavItemRecord> SelectClosure(HashSet<int> tiles, Action<uint>? beforeRead)
    {
        var selected = new Dictionary<uint, NavItemRecord>();
        var snapshot = new Dictionary<uint, NavItemRecord>();
        foreach (var id in inputs.ItemIds.Union(inputs.AppliedRecords.Keys))
        {
            beforeRead?.Invoke(id);
            snapshot[id] = inputs.Read(id)!;
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var (id, record) in snapshot)
            {
                if (selected.ContainsKey(id)) continue;
                var applied = inputs.AppliedRecords.GetValueOrDefault(id);
                if (!(Touches(applied, tiles) || Touches(record, tiles))) continue;
                selected.Add(id, record);
                if (applied?.Version == record.Version) continue;
                if (applied != null) foreach (var t in applied.Footprint) changed |= tiles.Add(t);
                foreach (var t in record.Footprint) changed |= tiles.Add(t);
            }
        } while (changed);
        return selected;
    }

    private Dictionary<int, List<NavItemRecord>> Covering(IEnumerable<NavItemRecord> records, HashSet<int> tiles)
    {
        var covering = new Dictionary<int, List<NavItemRecord>>();
        foreach (var record in records)
        {
            if (record.Removed || !settings.StacktoolLegacyCollision && record.Interaction == InteractionType.Stacktool) continue;
            foreach (var t in record.Footprint)
                if (tiles.Contains(t))
                {
                    if (!covering.TryGetValue(t, out var list)) covering[t] = list = new();
                    list.Add(record);
                }
        }
        return covering;
    }

    // A mode change rebuilds every tile; rooms with unmigrated surface-sensitive furniture stay at K=1.
    private void SwitchMode(IEnumerable<NavItemRecord> records, HashSet<int> tiles)
    {
        var layered = settings.LayeringEnabled && !records.Any(r => !r.Removed && LayeringEligibility.RequiresSingleSurface(r));
        if (layered == grid.Layered) return;
        if (layered) grid.EnterLayers(); else grid.LeaveLayers();
        for (var t = 0; t < grid.TileCount; t++) tiles.Add(t);
    }

    private static bool Touches(NavItemRecord? record, HashSet<int> tiles) => record != null && record.Footprint.Any(tiles.Contains);

    private CompatSurface CompileCompat(int t, List<NavItemRecord>? records)
    {
        var surface = CompatSurface.Resolve(grid, t, records);
        grid.LegacyZ[t] = surface.LegacyZ;
        var structuralStatus = Volatile.Read(ref grid.FloorStatusOverrides[t]);
        grid.LegacyFloorStatus[t] = structuralStatus >= 0 ? (byte)structuralStatus
            : (surface.Flags & NavFlags.Transit) != 0 ? (byte)1 : surface.Flags == NavFlags.None ? (byte)0 : (byte)3;
        if (grid.Layered) return surface;
        grid.TileVoid[t] = grid.BaseState[t] == SquareState.Blocked && surface.Flags == NavFlags.None;
        grid.WriteSurface(t, surface.Z, surface.Flags | LayeredTileCompiler.Lock(grid, t, structuralStatus), surface.Support,
            surface.Kind, surface.Group, 0, surface.Pillows, []);
        return surface;
    }
}

// Plus's single surface: the top item's effective kind, the walk magic tile, or the door (§5.4 compatibility mode).
internal readonly record struct CompatSurface(double Z, NavFlags Flags, uint Support, SurfaceKind Kind, int Group,
    int[] Pillows, double LegacyZ)
{
    internal static CompatSurface Resolve(NavGrid grid, int t, List<NavItemRecord>? records)
    {
        var floorZ = grid.BaseZ[t];
        var top = records?.MaxBy(r => (r.Top, r.Z, r.ItemId));
        var legacyZ = top?.Top ?? floorZ;
        if (t == grid.DoorTile) return new(grid.DoorZ, NavFlags.Door, 0, SurfaceKind.Door, 0, [], legacyZ);
        var walkMagic = records?.Where(r => r.Interaction == InteractionType.WalkMagicTile).MaxBy(r => (r.Z, r.ItemId));
        // The helper is the sole surface, even over void, seats or gates.
        if (walkMagic != null) return new(walkMagic.Z, NavFlags.Transit, walkMagic.ItemId, SurfaceKind.WalkMagic, 0, [], walkMagic.Z);
        if (top == null)
        {
            var floor = SurfaceRules.FloorCandidate(grid.BaseState[t], floorZ);
            return new(floorZ, floor?.Flags ?? NavFlags.None, 0, SurfaceKind.Floor, 0, [], legacyZ);
        }
        var item = SurfaceRules.ItemCandidate(top);
        // K=1 retains each pillow slot; ResolveClick selects the nearest acceptable one.
        var pillows = item.Kind == SurfaceKind.BedBase ? SurfaceRules.PillowRow(top, grid.Width) : [];
        return new(item.Z, item.Flags, item.Support, item.Kind, item.Group, pillows, item.Standable ? item.Z : legacyZ);
    }
}
