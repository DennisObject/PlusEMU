namespace Plus.HabboHotel.Rooms.PathFinding;

// Multi-surface index. With K=1 every tile has exactly its own slot and none of this is allocated.
public sealed partial class NavGrid
{
    private int[] _tileSlots = [];
    private byte[] _tileSurfaceCount = [];
    private int[] _overflowTile = [];
    private uint[][] _contacts;
    private readonly SortedSet<int> _freeOverflow = new();
    private readonly HashSet<SurfaceRef> _forcedOffGraph = new();
    private readonly Dictionary<int, SurfaceRef> _releasedSlots = new();
    private readonly Dictionary<int, SurfaceRef> _leftPrimaries = new();
    private int _overflowHighWater;

    public bool Layered { get; private set; }
    private long LayerIndexBytes => _tileSlots.Length * 4L + _tileSurfaceCount.Length + _overflowTile.Length * 4L
        + _contacts.Sum(contacts => (contacts?.Length ?? 0) * 4L);
    // Surfaces dropped by the >4 pinned overflow cap in the latest publish; their actors go off-graph.
    public IReadOnlySet<SurfaceRef> ForcedOffGraph => _forcedOffGraph;
    // Slots whose surface was removed or reassigned in the latest publish; their claims are stale.
    public IReadOnlyDictionary<int, SurfaceRef> ReleasedSlots => _releasedSlots;

    public int SurfaceCount(int tile) => Layered ? _tileSurfaceCount[tile] : 1;
    public int SurfaceAt(int tile, int ordinal) => Layered ? _tileSlots[tile * MaxSurfacesPerTile + ordinal] : tile;
    public int TileOf(int slot) => slot < TileCount ? slot : _overflowTile[slot - TileCount];
    public ReadOnlySpan<uint> Contacts(int slot) => _contacts[slot];

    // The tile's highest surface, or its own slot when it has none (interaction and teleport targets).
    public int TopSlot(int tile)
    {
        var count = SurfaceCount(tile);
        return count == 0 ? tile : SurfaceAt(tile, count - 1);
    }

    public int SlotOf(SurfaceRef surface)
    {
        if ((uint)surface.Tile >= (uint)TileCount) return -1;
        if (Layered)
            for (var ordinal = 0; ordinal < _tileSurfaceCount[surface.Tile]; ordinal++)
            {
                var slot = _tileSlots[surface.Tile * MaxSurfacesPerTile + ordinal];
                if (SupportItem[slot] == surface.SupportItemId && Kind[slot] == surface.Kind) return slot;
            }
        return Reference(surface.Tile) == surface ? surface.Tile : -1;
    }

    public int OwnerOf(int tile, uint itemId)
    {
        if (!Layered) return -1;
        for (var ordinal = 0; ordinal < _tileSurfaceCount[tile]; ordinal++)
        {
            var slot = _tileSlots[tile * MaxSurfacesPerTile + ordinal];
            if (Array.IndexOf(_contacts[slot], itemId) >= 0) return slot;
        }
        return -1;
    }

    internal ReadOnlySpan<int> TileSurfaces(int tile)
        => Layered ? _tileSlots.AsSpan(tile * MaxSurfacesPerTile, _tileSurfaceCount[tile]) : ReadOnlySpan<int>.Empty;

    internal void EnterLayers()
    {
        if (_tileSlots.Length == 0)
        {
            _tileSlots = new int[TileCount * MaxSurfacesPerTile];
            _tileSurfaceCount = new byte[TileCount];
        }
        for (var t = 0; t < TileCount; t++)
        {
            _tileSlots[t * MaxSurfacesPerTile] = t;
            _tileSurfaceCount[t] = (byte)(Active(t) ? 1 : 0);
        }
        Layered = true;
    }

    internal void LeaveLayers()
    {
        for (var t = 0; t < TileCount; t++)
            foreach (var slot in TileSurfaces(t))
                if (slot != t) ReleaseSlot(slot);
                else _leftPrimaries[t] = Reference(t);
        Layered = false;
    }

    // After the K=1 recompile, a primary slot that now holds a different surface is released too.
    internal void SettleLeftPrimaries()
    {
        foreach (var (slot, previous) in _leftPrimaries)
            if (!Active(slot) || Reference(slot) != previous) _releasedSlots.TryAdd(slot, previous);
        _leftPrimaries.Clear();
    }

    internal void SetTileSurfaces(int tile, ReadOnlySpan<int> slots)
    {
        slots.CopyTo(_tileSlots.AsSpan(tile * MaxSurfacesPerTile));
        _tileSurfaceCount[tile] = (byte)slots.Length;
    }

    internal int AllocateOverflow(int tile)
    {
        int slot;
        if (_freeOverflow.Count > 0) { slot = _freeOverflow.Min; _freeOverflow.Remove(slot); }
        else { slot = TileCount + _overflowHighWater++; EnsureSlotCapacity(slot + 1); }
        _overflowTile[slot - TileCount] = tile;
        return slot;
    }

    internal void ReleaseSlot(int slot)
    {
        if (Active(slot)) ActiveNodeCount--;
        _releasedSlots.TryAdd(slot, Reference(slot));
        Flags[slot] = NavFlags.None; _contacts[slot] = [];
        if (slot >= TileCount) _freeOverflow.Add(slot);
    }

    internal void WriteSurface(int slot, double z, NavFlags flags, uint support, SurfaceKind kind,
        int group, byte ordinal, int[] pillows, uint[] contacts)
    {
        var wasActive = Active(slot);
        WalkZ[slot] = z; Flags[slot] = flags; SupportItem[slot] = support; Kind[slot] = kind;
        GroupId[slot] = group; Ordinal[slot] = ordinal; PillowTiles[slot] = pillows; _contacts[slot] = contacts;
        ActiveNodeCount += (Active(slot) ? 1 : 0) - (wasActive ? 1 : 0);
    }

    internal void BeginPublish() { _forcedOffGraph.Clear(); _releasedSlots.Clear(); }
    internal void ForceOffGraph(SurfaceRef surface) => _forcedOffGraph.Add(surface);

    private void EnsureSlotCapacity(int slots)
    {
        if (slots <= WalkZ.Length) return;
        var length = Math.Max(slots, TileCount + Math.Max(16, (WalkZ.Length - TileCount) * 2));
        WalkZ = Grow(WalkZ, length); Flags = Grow(Flags, length); SupportItem = Grow(SupportItem, length);
        Ordinal = Grow(Ordinal, length); Kind = Grow(Kind, length); GroupId = Grow(GroupId, length);
        PillowTiles = Grow(PillowTiles, length); _contacts = Grow(_contacts, length);
        _overflowTile = Grow(_overflowTile, length - TileCount);
    }

    private static T[] Grow<T>(T[] array, int length)
    {
        Array.Resize(ref array, length);
        return array;
    }
}
