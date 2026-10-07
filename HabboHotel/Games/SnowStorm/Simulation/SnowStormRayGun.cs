namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// A Polaris ray gun (Plus extra, not in the official client): an <c>ads_igorraygun</c> fuse object facing 0, 2, 4 or 6.
/// Standing on <see cref="UseX"/>, <see cref="UseY"/> (the tile behind it) fires a burst of 7 default throws at the
/// point 15 tiles ahead of the gun.
/// </summary>
public sealed record SnowStormRayGun(int FuseObjectId, int X, int Y, int Direction, int UseX, int UseY)
{
    public const string FuseName = "ads_igorraygun";
    public const int BurstSize = 7;
    private const int BurstRange = 15;

    public int ForwardX => Direction switch
    {
        2 => 1,
        6 => -1,
        _ => 0
    };

    public int ForwardY => Direction switch
    {
        4 => 1,
        0 => -1,
        _ => 0
    };

    /// <summary>The gun of a fuse object, or null when it is not a ray gun facing a cardinal direction.</summary>
    public static SnowStormRayGun? From(SnowStormFuseObject fuseObject)
    {
        if (fuseObject.Name != FuseName || fuseObject.Direction is not (0 or 2 or 4 or 6)) {
            return null;
        }

        // Footprint dimensions swap for east/west facing, as in the AIR tile footprint rule.
        var (width, length) = fuseObject.Direction is 2 or 6
            ? (fuseObject.YDimension, fuseObject.XDimension)
            : (fuseObject.XDimension, fuseObject.YDimension);
        var gun = new SnowStormRayGun(fuseObject.Id, fuseObject.X, fuseObject.Y, fuseObject.Direction, 0, 0);

        return gun with
        {
            UseX = gun.X + (gun.ForwardX < 0 ? width : gun.ForwardX > 0 ? -1 : 0),
            UseY = gun.Y + (gun.ForwardY < 0 ? length : gun.ForwardY > 0 ? -1 : 0)
        };
    }

    /// <summary>Burst targets in tiles: the centre 15 tiles ahead, then (0,1), (1,0), (-1,1), (-1,-1), (1,-1), (1,1) around it.</summary>
    public IReadOnlyList<(int X, int Y)> BurstTargets()
    {
        int x = X + ForwardX * BurstRange;
        int y = Y + ForwardY * BurstRange;

        return [(x, y), (x, y + 1), (x + 1, y), (x - 1, y + 1), (x - 1, y - 1), (x + 1, y - 1), (x + 1, y + 1)];
    }
}
