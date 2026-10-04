using System.Runtime.CompilerServices;

namespace Plus.HabboHotel.Rooms.PathFinding;

public enum StepPurpose { Transit, Goal, Roller, Interaction }
public enum OccupancyView { Planning, Execution }
public enum StepReason { Ok, BoundsOrAdjacency, InteractionDenied, NotStandable, FloorLocked, TooHigh, TooLow, CornerVoid, CornerBlocked, GateDenied, Occupied }
public readonly record struct StepResult(StepReason Reason)
{
    public bool Ok => Reason == StepReason.Ok;
}
[Flags]
public enum TargetOccupancy : byte
{
    None = 0, Stationary = 1, Walking = 2, ExclusiveClaim = 4, GoalClaim = 8,
    SharedClaim = 16, RollerClaim = 32, OffGraph = 64
}

// P1 consumes snapshots of legacy membership. Claim bits are policy only; P2 owns claims.
public sealed class PlanningOccupancy(int capacity)
{
    public TargetOccupancy[] Targets { get; } = new TargetOccupancy[capacity];
    public long RetainedBytes => Targets.Length;
}

public static class ClaimMatrix
{
    public static TargetOccupancy BlockingMask(ActorProfile actor, NavFlags target, StepPurpose purpose, OccupancyView view)
    {
        if (actor.LegacyOverride || purpose == StepPurpose.Interaction) return TargetOccupancy.None;
        if (purpose == StepPurpose.Roller) return (TargetOccupancy)127;
        if (actor.IgnoreUsers || (target & NavFlags.Door) != 0) return TargetOccupancy.RollerClaim;
        if (!actor.Walkthrough) return view == OccupancyView.Execution ? (TargetOccupancy)127 : TargetOccupancy.Stationary | TargetOccupancy.OffGraph;
        if (purpose != StepPurpose.Goal) return TargetOccupancy.RollerClaim;
        return view == OccupancyView.Execution
            ? TargetOccupancy.Stationary | TargetOccupancy.GoalClaim | TargetOccupancy.RollerClaim | TargetOccupancy.OffGraph
            : TargetOccupancy.Stationary | TargetOccupancy.OffGraph;
    }
}

public sealed class MovementRules(NavGrid grid, PathfindingSettings settings, ActorAccessResolver? access = null)
{
    private readonly ActorAccessResolver _access = access ?? ActorAccessResolver.Cached;
    private readonly double _maxUp = settings.EffectiveMaxUp;
    private readonly double _maxDown = settings.EffectiveMaxDown ?? double.PositiveInfinity;
    private readonly CornerRule _cornerRule = settings.CornerRule;
    public StepResult CanStep(ActorProfile actor, in NavPosition from, in NavPosition to,
        StepPurpose purpose, OccupancyView view, PlanningOccupancy? occupancy = null)
    {
        if (!Adjacent(from, to)) return new(StepReason.BoundsOrAdjacency);
        return CanStepKnownNeighbour(actor, from, to, grid.Tile(to.X, to.Y), purpose, view, occupancy);
    }

    // A roller tile whose departing cargo leaves this cycle is the bare roller surface: next-roller
    // clearance already proved nothing staying rises above it (§14.9). Locks and occupancy still apply,
    // except an explicit floor status the departing cargo's cell rebuild releases (legacy lifetime).
    public StepResult CanRollOntoVacatedRoller(ActorProfile actor, in NavPosition from, in NavPosition to,
        PlanningOccupancy occupancy, bool floorStatusReleased = false)
    {
        if (!Adjacent(from, to)) return new(StepReason.BoundsOrAdjacency);
        var tile = grid.Tile(to.X, to.Y);
        var locked = floorStatusReleased ? Volatile.Read(ref grid.FloorLocks[tile]) != 0
            : (grid.Flags[tile] & NavFlags.FloorLocked) != 0;
        var flags = (locked ? NavFlags.FloorLocked : NavFlags.None) | NavFlags.Transit | NavFlags.Roller;
        return CanEnter(actor, from, to, tile, flags, StepPurpose.Roller, OccupancyView.Execution, occupancy);
    }

    private bool Adjacent(in NavPosition from, in NavPosition to)
    {
        var dx = to.X - from.X; var dy = to.Y - from.Y;
        return grid.InBounds(from.X, from.Y) && grid.InBounds(to.X, to.Y)
            && Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0);
    }

    // Search establishes bounds and adjacency before calling this shared policy kernel.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StepResult CanStepKnownNeighbour(ActorProfile actor, in NavPosition from, in NavPosition to,
        int tile, StepPurpose purpose, OccupancyView view, PlanningOccupancy? occupancy = null)
        => CanEnter(actor, from, to, tile, grid.Flags[tile], purpose, view, occupancy);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StepResult CanEnter(ActorProfile actor, in NavPosition from, in NavPosition to,
        int tile, NavFlags flags, StepPurpose purpose, OccupancyView view, PlanningOccupancy? occupancy)
    {
        if (actor.LegacyOverride) return new(StepReason.Ok);
        if (purpose == StepPurpose.Interaction)
            return new(actor.Interaction?.Allows(from, to) == true ? StepReason.Ok : StepReason.InteractionDenied);
        var required = purpose == StepPurpose.Transit ? NavFlags.Transit
            : NavFlags.Transit | NavFlags.GoalOnlySeat | NavFlags.GoalOnlyBed | NavFlags.Door;
        var standable = purpose == StepPurpose.Roller
            ? (flags & NavFlags.Transit) != 0 || grid.LegacyFloorStatus[tile] != 0
            : (flags & required) != 0;
        if (!standable) return new(StepReason.NotStandable);
        if ((flags & NavFlags.FloorLocked) != 0) return new(StepReason.FloorLocked);
        if (!actor.IgnoreStepHeight && purpose != StepPurpose.Roller)
        {
            var height = HeightReason(grid.WalkZ[tile] - from.Z);
            if (height != StepReason.Ok) return new(height);
        }
        if (from.X != to.X && from.Y != to.Y && _cornerRule != CornerRule.None)
        {
            var a = grid.Tile(to.X, from.Y); var b = grid.Tile(from.X, to.Y);
            // Official requires both flanks to exist even when the first is open.
            if (_cornerRule == CornerRule.Official && (grid.TileVoid[a] || grid.TileVoid[b])) return new(StepReason.CornerVoid);
            var openA = CanFlankKnownTile(actor, from.Z, a);
            if (_cornerRule == CornerRule.Strict
                ? !openA || !CanFlankKnownTile(actor, from.Z, b)
                : !openA && !CanFlankKnownTile(actor, from.Z, b)) return new(StepReason.CornerBlocked);
        }
        if ((flags & NavFlags.GuildGate) != 0 && !_access.CanEnterGuildGate(actor, grid.GroupId[tile])) return new(StepReason.GateDenied);
        if (occupancy != null && (occupancy.Targets[tile] & ClaimMatrix.BlockingMask(actor, flags, purpose, view)) != 0) return new(StepReason.Occupied);
        return new(StepReason.Ok);
    }

    public bool CanFlank(ActorProfile actor, in NavPosition from, int x, int y)
    {
        if (!grid.InBounds(x, y)) return false;
        return CanFlankKnownTile(actor, from.Z, grid.Tile(x, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanFlankKnownTile(ActorProfile actor, double fromZ, int t)
    {
        var flags = grid.Flags[t];
        return (flags & NavFlags.Transit) != 0 && (flags & NavFlags.FloorLocked) == 0
            && ((flags & NavFlags.GuildGate) == 0 || _access.CanEnterGuildGate(actor, grid.GroupId[t]))
            && (actor.IgnoreStepHeight || HeightReason(grid.WalkZ[t] - fromZ) == StepReason.Ok);
    }

    private StepReason HeightReason(double dz) => dz > _maxUp ? StepReason.TooHigh
        : -dz > _maxDown ? StepReason.TooLow : StepReason.Ok;
}
