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

// v2 rooms: committed members, off-graph members and every claim kind (X/G/S/R) on any surface of the tile.
internal sealed class ExecutorGateOccupancy(NavGrid grid, ClaimLedger claims) : IGateOccupancy
{
    private const long NoExcludedGroup = long.MinValue;

    public bool IsBlocked(IReadOnlyList<Point> footprint) => footprint.Any(IsTileBlocked);

    private bool IsTileBlocked(Point point)
    {
        if (!grid.InBounds(point.X, point.Y)) {
            return false;
        }

        var tile = grid.Tile(point.X, point.Y);

        if (Held(tile)) {
            return true;
        }

        for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++) {
            if (Held(grid.SurfaceAt(tile, ordinal))) {
                return true;
            }
        }

        return false;
    }

    private bool Held(int slot) => claims.OccupancyAt(slot, NoExcludedGroup) != TargetOccupancy.None;
}
