using System.Drawing;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Query port: is any tile of a gate footprint held by an actor or claim? One implementation per engine.
public interface IGateOccupancy
{
    bool IsBlocked(IReadOnlyList<Point> footprint);
}

// Legacy rooms only track actors per tile.
internal sealed class LegacyGateOccupancy(Gamemap map) : IGateOccupancy
{
    public bool IsBlocked(IReadOnlyList<Point> footprint) => footprint.Any(map.MapGotUser);
}

// v2 rooms: committed members, off-graph members and every claim kind (X/G/S/R) on the tile.
internal sealed class ExecutorGateOccupancy(NavGrid grid, ClaimLedger claims) : IGateOccupancy
{
    private const long NoExcludedGroup = long.MinValue;

    public bool IsBlocked(IReadOnlyList<Point> footprint) => footprint.Any(IsTileBlocked);

    private bool IsTileBlocked(Point tile)
        => grid.InBounds(tile.X, tile.Y)
            && claims.OccupancyAt(grid.Tile(tile.X, tile.Y), NoExcludedGroup) != TargetOccupancy.None;
}
