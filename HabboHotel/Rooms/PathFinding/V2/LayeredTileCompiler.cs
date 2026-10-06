namespace Plus.HabboHotel.Rooms.PathFinding;

// Layering mode (§5.4): filter by clearance, coalesce exactly coincident candidates, order, cap,
// then bind surfaces to stable slots (§5.3) and give every item on the tile one owning surface (§6.6).
internal sealed class LayeredTileCompiler(NavGrid grid, PathfindingSettings settings)
{
    private readonly int _cap = Math.Clamp(settings.MaxSurfacesPerTile, 1, NavGrid.MaxSurfacesPerTile);
    internal Func<SurfaceRef, bool>? SurfacePinned
    {
        get; set;
    }

    internal static NavFlags Lock(NavGrid grid, int t) => Lock(grid, t, Volatile.Read(ref grid.FloorStatusOverrides[t]));
    internal static NavFlags Lock(NavGrid grid, int t, int structuralStatus)
        => structuralStatus == 0 || Volatile.Read(ref grid.FloorLocks[t]) != 0 ? NavFlags.FloorLocked : NavFlags.None;

    internal void Compile(IReadOnlyDictionary<int, CompatSurface> compat, IReadOnlyDictionary<int, List<NavItemRecord>> covering)
    {
        var plans = new SortedDictionary<int, List<PlannedSurface>>();

        foreach (var (t, surface) in compat)
        {
            plans[t] = Plan(t, surface, covering.GetValueOrDefault(t) ?? []);
        }

        // Release every unmatched slot before allocating, so freed holes are reused within one publish.
        foreach (var (t, surfaces) in plans)
        {
            KeepOrRelease(t, surfaces);
        }

        foreach (var (t, surfaces) in plans)
        {
            Publish(t, surfaces, compat[t]);
        }
    }

    private List<PlannedSurface> Plan(int t, CompatSurface compat, List<NavItemRecord> items)
    {
        List<PlannedSurface> surfaces = compat.Kind is SurfaceKind.Door or SurfaceKind.WalkMagic
            ? [new(new(compat.Z, compat.Flags, compat.Kind, compat.Support, compat.Group, 0), [compat.Support], compat.Pillows)]
            : Cap(t, Coalesce(Candidates(t, items).Where(candidate => Clear(candidate, items))));
        AssignContacts(surfaces, items);

        return surfaces;
    }

    private IEnumerable<SurfaceCandidate> Candidates(int t, List<NavItemRecord> items)
    {
        if (SurfaceRules.FloorCandidate(grid.BaseState[t], grid.BaseZ[t]) is { } floor)
        {
            yield return floor;
        }

        foreach (var item in items)
        {
            var candidate = SurfaceRules.ItemCandidate(item);

            if (candidate.Standable)
            {
                yield return candidate;
            }
        }
    }

    // Keep a candidate at s only if no other item's blocking interval meets [s, s + clearance).
    private bool Clear(SurfaceCandidate candidate, List<NavItemRecord> items)
    {
        var headroom = candidate.Z + settings.AvatarClearance;

        foreach (var item in items)
        {
            if (item.ItemId == candidate.Item?.ItemId)
            {
                continue;
            }

            var (from, to) = SurfaceRules.BlockingInterval(item);

            if (from < to && from < headroom && candidate.Z < to)
            {
                return false;
            }
        }

        return true;
    }

    // Exact double equality only; the representative follows role precedence, then the higher item id.
    private List<PlannedSurface> Coalesce(IEnumerable<SurfaceCandidate> candidates) => candidates
        .GroupBy(candidate => candidate.Z).OrderBy(group => group.Key).Select(group =>
        {
            var representative = group.MaxBy(candidate => (candidate.Role, candidate.Support));
            var pillows = representative.Kind == SurfaceKind.BedBase ? SurfaceRules.PillowRow(representative.Item!, grid.Width) : [];

            return new PlannedSurface(representative, group.Where(c => c.Item != null).Select(c => c.Support).ToList(), pillows);
        }).ToList();

    private List<PlannedSurface> Cap(int t, List<PlannedSurface> surfaces)
    {
        if (surfaces.Count <= _cap)
        {
            return surfaces;
        }

        var pinned = surfaces.Where(surface => SurfacePinned?.Invoke(surface.Reference(t)) == true).ToList();
        var keep = pinned.Count > NavGrid.MaxSurfacesPerTile
            ? pinned.OrderByDescending(surface => surface.Z).Take(NavGrid.MaxSurfacesPerTile)
            : pinned.Concat(surfaces.Except(pinned).OrderByDescending(surface => surface.Z).Take(Math.Max(0, _cap - pinned.Count)));
        var kept = keep.OrderBy(surface => surface.Z).ToList();

        foreach (var dropped in pinned.Except(kept))
        {
            grid.ForceOffGraph(dropped.Reference(t));
        }

        return kept;
    }

    // An item is owned by its own surface, else by the highest surface at or below its base, else the lowest.
    private static void AssignContacts(List<PlannedSurface> surfaces, List<NavItemRecord> items)
    {
        if (surfaces.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            var owner = surfaces.FirstOrDefault(surface => surface.Members.Contains(item.ItemId))
                ?? surfaces.LastOrDefault(surface => surface.Z <= item.Z + SurfaceRules.Epsilon) ?? surfaces[0];
            owner.Contacts.Add(item.ItemId);
        }
    }

    private void KeepOrRelease(int t, List<PlannedSurface> surfaces)
    {
        foreach (var slot in grid.TileSurfaces(t).ToArray())
        {
            var reference = grid.Reference(slot);
            var match = surfaces.FirstOrDefault(surface => surface.Slot < 0 && surface.Reference(t) == reference);

            if (match != null)
            {
                match.Slot = slot;
            }
            else
            {
                grid.ReleaseSlot(slot);
            }
        }
    }

    private void Publish(int t, List<PlannedSurface> surfaces, CompatSurface compat)
    {
        var slots = new int[surfaces.Count];
        var ownSlotTaken = surfaces.Any(surface => surface.Slot == t);
        var locked = Lock(grid, t);

        for (var ordinal = 0; ordinal < surfaces.Count; ordinal++)
        {
            var surface = surfaces[ordinal];

            if (surface.Slot < 0)
            {
                surface.Slot = ownSlotTaken ? grid.AllocateOverflow(t) : t;
                ownSlotTaken = true;
            }

            grid.WriteSurface(surface.Slot, surface.Z, surface.Flags | locked, surface.Support, surface.Kind,
                surface.Group, (byte)ordinal, surface.Pillows, surface.Contacts.ToArray());
            slots[ordinal] = surface.Slot;
        }

        grid.SetTileSurfaces(t, slots);
        grid.TileVoid[t] = grid.BaseState[t] == SquareState.Blocked && surfaces.Count == 0;

        // A tile's own slot without a surface keeps the compatibility anchor for interaction targets.
        if (!ownSlotTaken)
        {
            grid.WriteSurface(t, compat.Z, NavFlags.None, compat.Support, compat.Kind, 0, 0, [], []);
        }
    }

    private sealed class PlannedSurface(SurfaceCandidate representative, List<uint> members, int[] pillows)
    {
        public double Z => representative.Z;
        public NavFlags Flags => representative.Flags;
        public SurfaceKind Kind => representative.Kind;
        public uint Support => representative.Support;
        public int Group => representative.Group;
        public List<uint> Members { get; } = members;
        public List<uint> Contacts { get; } = new();
        public int[] Pillows { get; } = pillows;
        public int Slot { get; set; } = -1;
        public SurfaceRef Reference(int tile) => new(tile, Support, Kind);
    }
}
