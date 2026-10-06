namespace Plus.HabboHotel.Rooms.PathFinding;

// Picks one of a tile's surfaces for an explicit Z. Shared by forced placement, rebinding,
// roller landing and contact ownership so every caller resolves layers the same way.
internal static class SurfaceSelection
{
    internal const double Tolerance = 0.001;

    // Highest matching surface, or -1. K=1 considers only the tile's own slot.
    internal static int Select(NavGrid grid, int tile, double z, ForceResolution resolution)
    {
        for (var ordinal = grid.SurfaceCount(tile) - 1; ordinal >= 0; ordinal--)
        {
            var slot = grid.SurfaceAt(tile, ordinal);

            if (!grid.Active(slot))
            {
                continue;
            }

            var matches = resolution switch
            {
                ForceResolution.ExactZ => Math.Abs(grid.WalkZ[slot] - z) <= Tolerance,
                ForceResolution.NearestAtOrBelow => grid.WalkZ[slot] <= z + Tolerance,
                _ => true
            };

            if (matches)
            {
                return slot;
            }
        }

        return -1;
    }

    // The surface something at z rests on: the highest at or below it, else the lowest.
    internal static int Resting(NavGrid grid, int tile, double z)
    {
        var below = Select(grid, tile, z, ForceResolution.NearestAtOrBelow);

        return below >= 0 || grid.SurfaceCount(tile) == 0 ? below : grid.SurfaceAt(tile, 0);
    }
}
