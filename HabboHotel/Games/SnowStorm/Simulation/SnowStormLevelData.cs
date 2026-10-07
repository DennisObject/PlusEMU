namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// AIR <c>GameLevelData</c>: arena size, heightmap rows joined by <c>\r</c> (<c>x</c> = no tile) and the fuse objects,
/// in wire order.
/// </summary>
public sealed record SnowStormLevelData(int Width, int Height, string HeightMap, IReadOnlyList<SnowStormFuseObject> FuseObjects);

/// <summary>
/// AIR <c>FuseObjectData</c>, in wire order. <see cref="Height"/> is in world units (added to the tile height that
/// snowballs hit); <see cref="Stuff"/> is the legacy string furni state.
/// </summary>
public sealed record SnowStormFuseObject(
    string Name,
    int Id,
    int X,
    int Y,
    int XDimension,
    int YDimension,
    int Height,
    int Direction,
    int Altitude,
    bool CanStandOn,
    string Stuff);
