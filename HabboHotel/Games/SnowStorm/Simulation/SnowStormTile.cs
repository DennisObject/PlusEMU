namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

// AIR Tile: one occupying game object, fuse objects, summed height and the blocked flag.
internal sealed class SnowStormTile(int x, int y)
{
    private readonly SnowStormTile?[] _neighbours = new SnowStormTile?[8];
    private readonly List<SnowStormFuseObject> _fuseObjects = [];

    public int X { get; } = x;

    public int Y { get; } = y;

    public int WorldX { get; } = x * SnowStormMath.TileWidth;

    public int WorldY { get; } = y * SnowStormMath.TileWidth;

    public int Height { get; private set; }

    public bool Blocked { get; set; }

    public SnowStormGameObject? GameObject { get; private set; }

    public SnowStormHuman? OccupyingHuman => GameObject as SnowStormHuman;

    public void AddFuseObject(SnowStormFuseObject fuseObject)
    {
        _fuseObjects.Add(fuseObject);
        AddToHeight(fuseObject.Height);
    }

    public void AddToHeight(int height)
    {
        Height += height;

        if (Height < 0) {
            Height = 0;
        }
    }

    public void LinkTile(SnowStormTile tile, int direction8)
    {
        _neighbours[direction8] = tile;
        tile._neighbours[SnowStormMath.RotateDirection8(direction8, 4)] = this;
    }

    public SnowStormTile? GetTileInDirection(int direction8) => _neighbours[direction8];

    public bool LocationIsInTileRange(int x, int y) =>
        SnowStormMath.Abs(WorldX - x) < SnowStormMath.TileHalfWidth && SnowStormMath.Abs(WorldY - y) < SnowStormMath.TileHalfWidth;

    public bool IsLocation(int x, int y) => WorldX == x && WorldY == y;

    // Ghost objects are not simulated server-side, so the AIR own-ghost exception is not needed.
    public bool CanMoveTo()
    {
        bool fuseBlocked = _fuseObjects.Count > 1 || (_fuseObjects.Count == 1 && !_fuseObjects[0].CanStandOn);

        return !fuseBlocked && GameObject == null && !Blocked;
    }

    public void AddGameObject(SnowStormGameObject gameObject) => GameObject ??= gameObject;

    public void RemoveGameObject() => GameObject = null;

    public void RemoveOccupyingHuman()
    {
        if (OccupyingHuman != null) {
            GameObject = null;
        }
    }
}
