using System.Runtime.CompilerServices;

namespace Plus.HabboHotel.Rooms.PathFinding;

public enum StepPurpose
{
    Transit, Goal, Roller, Interaction
}
public enum OccupancyView
{
    Planning, Execution
}
public enum StepReason
{
    Ok, BoundsOrAdjacency, InteractionDenied, NotStandable, FloorLocked, TooHigh, TooLow, CornerVoid, CornerBlocked, GateDenied, Occupied
}
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
        if (actor.LegacyOverride || purpose == StepPurpose.Interaction) {
            return TargetOccupancy.None;
        }

        if (purpose == StepPurpose.Roller) {
            return (TargetOccupancy)127;
        }

        if (actor.IgnoreUsers || (target & NavFlags.Door) != 0) {
            return TargetOccupancy.RollerClaim;
        }

        if (!actor.Walkthrough) {
            return view == OccupancyView.Execution ? (TargetOccupancy)127 : TargetOccupancy.Stationary | TargetOccupancy.OffGraph;
        }

        if (purpose != StepPurpose.Goal) {
            return TargetOccupancy.RollerClaim;
        }

        return view == OccupancyView.Execution
            ? TargetOccupancy.Stationary | TargetOccupancy.GoalClaim | TargetOccupancy.RollerClaim | TargetOccupancy.OffGraph
            : TargetOccupancy.Stationary | TargetOccupancy.OffGraph;
    }

    // The claim an announced step takes on its target (§6.4).
    public static ClaimKind KindFor(ActorProfile actor, NavFlags target, StepPurpose purpose)
    {
        if (actor.LegacyOverride || actor.IgnoreUsers || (target & NavFlags.Door) != 0) {
            return ClaimKind.Shared;
        }

        return !actor.Walkthrough ? ClaimKind.Exclusive : purpose == StepPurpose.Goal ? ClaimKind.Goal : ClaimKind.Shared;
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
        if (!Adjacent(from, to)) {
            return new(StepReason.BoundsOrAdjacency);
        }

        return CanStepKnownNeighbour(actor, from, to, to.Slot >= 0 ? to.Slot : grid.Tile(to.X, to.Y), purpose, view, occupancy);
    }

    // A roller tile whose departing cargo leaves this cycle is the bare roller surface: next-roller
    // clearance already proved nothing staying rises above it (§14.9). Locks and occupancy still apply,
    // except an explicit floor status the departing cargo's cell rebuild releases (legacy lifetime).
    public StepResult CanRollOntoVacatedRoller(ActorProfile actor, in NavPosition from, in NavPosition to,
        PlanningOccupancy occupancy, bool floorStatusReleased = false)
    {
        if (!Adjacent(from, to)) {
            return new(StepReason.BoundsOrAdjacency);
        }

        // The target surface supplies flags and occupancy; the floor lock is tile-scoped.
        var slot = to.Slot >= 0 ? to.Slot : grid.Tile(to.X, to.Y);
        var locked = floorStatusReleased ? Volatile.Read(ref grid.FloorLocks[grid.TileOf(slot)]) != 0
            : (grid.Flags[slot] & NavFlags.FloorLocked) != 0;
        var flags = (locked ? NavFlags.FloorLocked : NavFlags.None) | NavFlags.Transit | NavFlags.Roller;

        return CanEnter(actor, from, to, slot, flags, StepPurpose.Roller, OccupancyView.Execution, occupancy);
    }

    private bool Adjacent(in NavPosition from, in NavPosition to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        return grid.InBounds(from.X, from.Y) && grid.InBounds(to.X, to.Y)
            && Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0);
    }

    // Search establishes bounds and adjacency before calling this shared policy kernel.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StepResult CanStepKnownNeighbour(ActorProfile actor, in NavPosition from, in NavPosition to,
        int slot, StepPurpose purpose, OccupancyView view, PlanningOccupancy? occupancy = null)
        => CanEnter(actor, from, to, slot, grid.Flags[slot], purpose, view, occupancy);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StepResult CanEnter(ActorProfile actor, in NavPosition from, in NavPosition to,
        int slot, NavFlags flags, StepPurpose purpose, OccupancyView view, PlanningOccupancy? occupancy)
    {
        if (actor.LegacyOverride) {
            return new(StepReason.Ok);
        }

        if (purpose == StepPurpose.Interaction) {
            return new(actor.Interaction?.Allows(from, to) == true ? StepReason.Ok : StepReason.InteractionDenied);
        }

        if (!IsStandable(flags, slot, purpose)) {
            return new(StepReason.NotStandable);
        }

        if ((flags & NavFlags.FloorLocked) != 0) {
            return new(StepReason.FloorLocked);
        }

        if (!actor.IgnoreStepHeight && purpose != StepPurpose.Roller) {
            var height = HeightReason(grid.WalkZ[slot] - from.Z);

            if (height != StepReason.Ok) {
                return new(height);
            }
        }

        if (from.X != to.X && from.Y != to.Y && _cornerRule != CornerRule.None) {
            var corner = CornerReason(actor, from, to);

            if (corner != StepReason.Ok) {
                return new(corner);
            }
        }

        if ((flags & NavFlags.GuildGate) != 0 && !_access.CanEnterGuildGate(actor, grid.GroupId[slot])) {
            return new(StepReason.GateDenied);
        }

        if (occupancy != null && (occupancy.Targets[slot] & ClaimMatrix.BlockingMask(actor, flags, purpose, view)) != 0) {
            return new(StepReason.Occupied);
        }

        return new(StepReason.Ok);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsStandable(NavFlags flags, int slot, StepPurpose purpose)
    {
        var required = purpose == StepPurpose.Transit ? NavFlags.Transit
            : NavFlags.Transit | NavFlags.GoalOnlySeat | NavFlags.GoalOnlyBed | NavFlags.Door;

        return purpose == StepPurpose.Roller
            ? (flags & NavFlags.Transit) != 0 || grid.LegacyFloorStatus[grid.TileOf(slot)] != 0
            : (flags & required) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StepReason CornerReason(ActorProfile actor, in NavPosition from, in NavPosition to)
    {
        var a = grid.Tile(to.X, from.Y);
        var b = grid.Tile(from.X, to.Y);

        // Official requires both flanks to exist even when the first is open.
        if (_cornerRule == CornerRule.Official && (grid.TileVoid[a] || grid.TileVoid[b])) {
            return StepReason.CornerVoid;
        }

        var openA = CanFlankKnownTile(actor, from.Z, a);

        if (_cornerRule == CornerRule.Strict
            ? !openA || !CanFlankKnownTile(actor, from.Z, b)
            : !openA && !CanFlankKnownTile(actor, from.Z, b)) {
            return StepReason.CornerBlocked;
        }

        return StepReason.Ok;
    }

    public bool CanFlank(ActorProfile actor, in NavPosition from, int x, int y)
    {
        if (!grid.InBounds(x, y)) {
            return false;
        }

        return CanFlankKnownTile(actor, from.Z, grid.Tile(x, y));
    }

    // A flank is open if some surface on it is open (§5.5 CanFlank).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanFlankKnownTile(ActorProfile actor, double fromZ, int t)
    {
        for (var ordinal = 0; ordinal < grid.SurfaceCount(t); ordinal++) {
            if (FlankSurfaceOpen(actor, fromZ, grid.SurfaceAt(t, ordinal))) {
                return true;
            }
        }

        return false;
    }

    private bool FlankSurfaceOpen(ActorProfile actor, double fromZ, int slot)
    {
        var flags = grid.Flags[slot];

        return (flags & NavFlags.Transit) != 0 && (flags & NavFlags.FloorLocked) == 0
            && ((flags & NavFlags.GuildGate) == 0 || _access.CanEnterGuildGate(actor, grid.GroupId[slot]))
            && (actor.IgnoreStepHeight || HeightReason(grid.WalkZ[slot] - fromZ) == StepReason.Ok);
    }

    private StepReason HeightReason(double dz) => dz > _maxUp ? StepReason.TooHigh
        : -dz > _maxDown ? StepReason.TooLow : StepReason.Ok;
}
