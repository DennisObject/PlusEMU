namespace Plus.HabboHotel.Rooms.PathFinding;

[Flags]
public enum NavFlags : ushort
{
    None = 0, Transit = 1, GoalOnlySeat = 2, GoalOnlyBed = 4, Door = 8,
    GuildGate = 16, Roller = 32, FloorLocked = 64, ModelSeat = 128
}
public enum SurfaceKind : byte { Floor, Top, SeatBase, BedBase, Door, GateBase, WalkMagic }
public readonly record struct SurfaceRef(int Tile, uint SupportItemId, SurfaceKind Kind);
public readonly record struct NavPosition(int X, int Y, double Z, int Slot = -1);

public sealed class NavGrid
{
    public int Width { get; }
    public int Height { get; }
    public int SlotCapacity => Width * Height; // K=1: tile slots never compact.
    public int ActiveNodeCount { get; internal set; }
    public int Version { get; internal set; }
    public double[] WalkZ { get; }
    public NavFlags[] Flags { get; }
    public uint[] SupportItem { get; }
    public byte[] Ordinal { get; }
    public SurfaceKind[] Kind { get; }
    public int[] GroupId { get; }
    internal int[][] PillowTiles { get; }
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
            || baseZ.Length != width * height || baseState.Length != width * height)
            throw new ArgumentOutOfRangeException(nameof(width), "V2 requires a 1..256 tile map on each axis.");
        Width = width; Height = height;
        BaseZ = (double[])baseZ.Clone(); BaseState = (SquareState[])baseState.Clone();
        DoorTile = doorTile; DoorZ = doorZ;
        WalkZ = new double[SlotCapacity]; LegacyZ = new double[SlotCapacity];
        Flags = new NavFlags[SlotCapacity]; SupportItem = new uint[SlotCapacity];
        Ordinal = new byte[SlotCapacity]; Kind = new SurfaceKind[SlotCapacity];
        GroupId = new int[SlotCapacity]; PillowTiles = new int[SlotCapacity][]; TileVoid = new bool[SlotCapacity];
        FloorLocks = new int[SlotCapacity]; FloorStatusOverrides = new int[SlotCapacity];
        Array.Fill(FloorStatusOverrides, -1); LegacyFloorStatus = new byte[SlotCapacity];
        Connectivity = new(this);
    }

    public bool InBounds(int x, int y) => (uint)x < Width && (uint)y < Height;
    public int Tile(int x, int y) => y * Width + x;
    public bool Active(int slot) => (Flags[slot] & ~NavFlags.FloorLocked) != 0;
    public SurfaceRef Reference(int slot) => new(slot, SupportItem[slot], Kind[slot]);
    public NavPosition Position(int slot, bool legacy = false) => new(slot % Width, slot / Width,
        legacy ? LegacyZ[slot] : WalkZ[slot], slot);
    public long RetainedBytes => SlotCapacity * (8L * 3 + 2 + 4 + 1 + 1 + 4 + 1 + 4 + 4) + SlotCapacity * 13L + PillowTiles.Sum(p => (p?.Length ?? 0) * 4L) + Connectivity.RetainedBytes;
}
