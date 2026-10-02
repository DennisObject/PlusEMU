using Plus.HabboHotel.Items.Wired.Modern.Selectors;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

public sealed record WiredMovementOptions(int AnimationTimeMs, bool Animate, double Height,
    int? Rotation, int CurveType, int CurveIntensity, int CurveStrength, int AnimationDistanceOffset);

/// <summary>Shared calculations for actual furniture/avatar movement callbacks.</summary>
public static class WiredMovementPolicy
{
    public static WiredMovementOptions Resolve(WiredAddonPolicy policy, WiredSelectorFurniture mover,
        int targetX, int targetY, double targetZ, int? rotation = null, bool explicitHeight = false)
    {
        var projectile = policy.Projectile;
        var scoped = projectile?.ItemIds.Contains(mover.Id) == true;
        var direction = scoped && projectile!.DirectionSystem is int system
            ? Direction(system, (long)targetX - mover.X, (long)targetY - mover.Y) : null;
        var offset = scoped ? projectile!.Distance switch
        {
            WiredProjectileDistance.Overshoot => projectile.DistanceTiles,
            WiredProjectileDistance.Fixed => (int)Math.Clamp(projectile.DistanceTiles
                - Math.Max(Math.Abs((long)targetX - mover.X), Math.Abs((long)targetY - mover.Y)), int.MinValue, int.MaxValue),
            _ => 0
        } : 0;
        return new(policy.AnimationTimeMs, !policy.DisableAnimation,
            policy.Physics?.KeepAltitude == true && !explicitHeight ? mover.Z : targetZ,
            direction is int d ? (d + projectile!.RotationOffset) % 8 : rotation,
            policy.Curve?.Type ?? (scoped && projectile!.CurveStrength is not null ? 7 : 0), policy.Curve?.Intensity ?? 100,
            policy.Curve?.Strength ?? (scoped ? projectile!.CurveStrength ?? 0 : 0), offset);
    }

    public static int? Direction(int system, long dx, long dy)
    {
        if (dx == 0 && dy == 0) return null;
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);
        var diagonal = system switch
        {
            0 => ax != 0 && ay != 0,
            1 => Math.Min(ax, ay) > Math.Max(ax, ay) * 0.41421356237309503,
            2 or 3 => false,
            _ => throw new ArgumentOutOfRangeException(nameof(system))
        };
        if (diagonal) return dx > 0 ? dy > 0 ? 3 : 1 : dy > 0 ? 5 : 7;
        var horizontal = system == 2 ? ax > ay : ax >= ay;
        return horizontal ? dx > 0 ? 2 : 6 : dy > 0 ? 4 : 0;
    }

    public static bool IsBlocked(WiredPhysicsPolicy? physics, IEnumerable<uint> collidingFurni,
        IEnumerable<int> collidingUsers, bool normallyBlockedByFurni, bool normallyBlockedByUsers)
    {
        var items = collidingFurni.ToArray();
        if (items.Any(x => physics?.BlockingFurni.Contains(x) == true)) return true;
        return normallyBlockedByFurni && items.Any(x => physics?.ThroughFurni.Contains(x) != true)
            || normallyBlockedByUsers && collidingUsers.Any(x => physics?.ThroughUsers.Contains(x) != true);
    }

    public static IReadOnlyList<WiredSelectorAvatar> CarriedUsers(WiredCarryPolicy? carry,
        WiredSelectorFurniture mover, WiredSelectorWorld world, Func<int, uint, bool> isStandingOn)
    {
        if (carry is null) return [];
        return world.Users.Where(x => carry.UserIds.Contains(x.Id)
            && (carry.SameTile ? mover.Tiles.Contains((x.X, x.Y)) : isStandingOn(x.Id, mover.Id))).ToArray();
    }
}
