using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class NavGridCompiler(NavGrid grid, NavInputs inputs, PathfindingSettings settings)
{
    public void ApplyNow() => Apply();
    public void RebuildAll() { inputs.MarkAllDirty(); Apply(); }

    internal void Apply(Action? afterDrain = null, Action<uint>? beforeRead = null)
    {
        var tiles = inputs.Drain();
        afterDrain?.Invoke();
        if (tiles.Count == 0) return;
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
        var records = new Dictionary<uint, NavItemRecord>(inputs.AppliedRecords);
        foreach (var (id, record) in selected) records[id] = record;
        var covering = new Dictionary<int, List<NavItemRecord>>();
        foreach (var record in records.Values)
        {
            if (record.Removed || !settings.StacktoolLegacyCollision && record.Interaction == InteractionType.Stacktool) continue;
            foreach (var t in record.Footprint)
                if (tiles.Contains(t))
                {
                    if (!covering.TryGetValue(t, out var list)) covering[t] = list = new();
                    list.Add(record);
                }
        }
        foreach (var t in tiles) Compile(t, covering.GetValueOrDefault(t));
        foreach (var (id, record) in selected) inputs.AppliedRecords[id] = record;
        grid.Version++;
    }

    private static bool Touches(NavItemRecord? record, HashSet<int> tiles) => record != null && record.Footprint.Any(tiles.Contains);

    private void Compile(int t, List<NavItemRecord>? records)
    {
        var wasActive = grid.Active(t);
        var flags = NavFlags.None;
        uint support = 0;
        var kind = SurfaceKind.Floor;
        var z = grid.BaseZ[t];
        var group = 0;
        var top = records?.MaxBy(r => (r.Top, r.Z, r.ItemId));
        grid.PillowTiles[t] = Array.Empty<int>();
        grid.LegacyZ[t] = top?.Top ?? z;
        // TODO(P2 WalkMagicTile): before choosing top, compile the highest walk magic
        // record as the sole Transit surface at its base Z (highest item id breaks ties).
        // InteractionType.WalkMagicTile is supplied by the parallel magic-tile PR.
        if (t == grid.DoorTile)
        {
            flags = NavFlags.Door; kind = SurfaceKind.Door; z = grid.DoorZ;
        }
        else if (top != null)
        {
            support = top.ItemId;
            z = top.Top; kind = SurfaceKind.Top;
            if (top.Seat || top.Interaction is InteractionType.Bed or InteractionType.TentSmall)
            {
                z = top.Z;
                flags = top.Seat ? NavFlags.GoalOnlySeat : NavFlags.GoalOnlyBed;
                kind = top.Seat ? SurfaceKind.SeatBase : SurfaceKind.BedBase;
                if (!top.Seat)
                {
                    // Pillow row is the item's leading footprint row. K=1 retains each
                    // pillow slot; ResolveClick selects the nearest acceptable one.
                    var row = top.Rotation is 2 or 6 ? top.Footprint.Min(p => p % grid.Width) : top.Footprint.Min(p => p / grid.Width);
                    grid.PillowTiles[t] = top.Footprint.Where(p => (top.Rotation is 2 or 6 ? p % grid.Width : p / grid.Width) == row).ToArray();
                }
            }
            else if (top.Interaction == InteractionType.GuildGate)
            {
                flags = NavFlags.Transit | NavFlags.GuildGate; kind = SurfaceKind.GateBase; group = top.GroupId;
            }
            else if (top.Walkable || top.Interaction == InteractionType.Gate && top.State == "1")
                flags = NavFlags.Transit;
            if (top.Interaction == InteractionType.Roller && flags.HasFlag(NavFlags.Transit)) flags |= NavFlags.Roller;
            if (flags != NavFlags.None) grid.LegacyZ[t] = z;
        }
        else if (grid.BaseState[t] == SquareState.Open) flags = NavFlags.Transit;
        else if (grid.BaseState[t] == SquareState.Seat) flags = NavFlags.GoalOnlySeat | NavFlags.ModelSeat;
        grid.TileVoid[t] = grid.BaseState[t] == SquareState.Blocked && flags == NavFlags.None;
        var structuralStatus = Volatile.Read(ref grid.FloorStatusOverrides[t]);
        grid.LegacyFloorStatus[t] = structuralStatus >= 0 ? (byte)structuralStatus
            : (flags & NavFlags.Transit) != 0 ? (byte)1 : flags == NavFlags.None ? (byte)0 : (byte)3;
        if (structuralStatus == 0 || Volatile.Read(ref grid.FloorLocks[t]) != 0) flags |= NavFlags.FloorLocked;
        grid.Flags[t] = flags; grid.WalkZ[t] = z; grid.SupportItem[t] = support;
        grid.Kind[t] = kind; grid.GroupId[t] = group;
        grid.ActiveNodeCount += (grid.Active(t) ? 1 : 0) - (wasActive ? 1 : 0);
    }
}
