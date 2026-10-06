namespace Plus.HabboHotel.Rooms.PathFinding;

[Flags]
public enum NavFlags : ushort
{
    None = 0, Transit = 1, GoalOnlySeat = 2, GoalOnlyBed = 4, Door = 8,
    GuildGate = 16, Roller = 32, FloorLocked = 64, ModelSeat = 128
}
public enum SurfaceKind : byte
{
    Floor, Top, SeatBase, BedBase, Door, GateBase, WalkMagic
}
public readonly record struct SurfaceRef(int Tile, uint SupportItemId, SurfaceKind Kind);
public readonly record struct NavPosition(int X, int Y, double Z, int Slot = -1);

public sealed partial class NavGrid
{
    public const int MaxSurfacesPerTile = 4;
    public int Width { get; }
    public int Height { get; }
    public int TileCount => Width * Height;
    // Slot t belongs to tile t; further layered surfaces use overflow slots that never compact.
    public int SlotCapacity => TileCount + _overflowHighWater;
    public int ActiveNodeCount { get; internal set; }
    public int Version { get; internal set; }
    public double[] WalkZ { get; private set; }
    public NavFlags[] Flags { get; private set; }
    public uint[] SupportItem { get; private set; }
    public byte[] Ordinal { get; private set; }
    public SurfaceKind[] Kind { get; private set; }
    public int[] GroupId { get; private set; }
    internal int[][] PillowTiles { get; private set; }
    public bool[] TileVoid { get; }
    public double[] LegacyZ { get; }
    public byte[] LegacyFloorStatus { get; }
    internal int[] FloorStatusOverrides { get; }
    internal double[] BaseZ { get; }
    internal SquareState[] BaseState { get; }
    internal int DoorTile { get; }
    internal double DoorZ { get; }
    internal int[] FloorLocks { get; }
    public Connectivity Connectivity { get; }

    public NavGrid(int width, int height, double[] baseZ, SquareState[] baseState,
        int doorTile = -1, double doorZ = 0)
    {
        if (width is < 1 or > 256 || height is < 1 or > 256
            || baseZ.Length != width * height || baseState.Length != width * height) {
            throw new ArgumentOutOfRangeException(nameof(width), "V2 requires a 1..256 tile map on each axis.");
        }

        Width = width;
        Height = height;
        BaseZ = (double[])baseZ.Clone();
        BaseState = (SquareState[])baseState.Clone();
        DoorTile = doorTile;
        DoorZ = doorZ;
        WalkZ = new double[TileCount];
        LegacyZ = new double[TileCount];
        Flags = new NavFlags[TileCount];
        SupportItem = new uint[TileCount];
        Ordinal = new byte[TileCount];
        Kind = new SurfaceKind[TileCount];
        GroupId = new int[TileCount];
        PillowTiles = new int[TileCount][];
        TileVoid = new bool[TileCount];
        _contacts = new uint[TileCount][];
        FloorLocks = new int[TileCount];
        FloorStatusOverrides = new int[TileCount];
        Array.Fill(FloorStatusOverrides, -1);
        LegacyFloorStatus = new byte[TileCount];
        Connectivity = new(this);
    }

    public bool InBounds(int x, int y) => (uint)x < Width && (uint)y < Height;
    public int Tile(int x, int y) => y * Width + x;
    public bool Active(int slot) => (Flags[slot] & ~NavFlags.FloorLocked) != 0;
    public SurfaceRef Reference(int slot) => new(TileOf(slot), SupportItem[slot], Kind[slot]);
    public NavPosition Position(int slot, bool legacy = false)
    {
        var tile = TileOf(slot);

        return new(tile % Width, tile / Width, legacy ? LegacyZ[tile] : WalkZ[slot], slot);
    }
    public long RetainedBytes => SlotCapacity * (8L * 3 + 2 + 4 + 1 + 1 + 4 + 1 + 4 + 4) + SlotCapacity * 13L + PillowTiles.Sum(p => (p?.Length ?? 0) * 4L)
        + LayerIndexBytes + Connectivity.RetainedBytes;
}
