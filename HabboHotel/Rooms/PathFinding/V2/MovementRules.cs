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

public sealed class MovementRules(NavGrid grid, PathfindingSettings settings)
{
    private readonly double _maxUp = settings.EffectiveMaxUp;
    private readonly double? _maxDown = settings.EffectiveMaxDown;
    public StepResult CanStep(ActorProfile actor, in NavPosition from, in NavPosition to,
        StepPurpose purpose, OccupancyView view, PlanningOccupancy? occupancy = null)
    {
        var dx = to.X - from.X; var dy = to.Y - from.Y;
        if (!grid.InBounds(from.X, from.Y) || !grid.InBounds(to.X, to.Y)
            || Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || dx == 0 && dy == 0)
            return new(StepReason.BoundsOrAdjacency);
        if (actor.LegacyOverride) return new(StepReason.Ok);
        if (purpose == StepPurpose.Interaction)
            return new(actor.Interaction?.Allows(from, to) == true ? StepReason.Ok : StepReason.InteractionDenied);
        var tile = grid.Tile(to.X, to.Y);
        var flags = grid.Flags[tile];
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
        if (dx != 0 && dy != 0 && settings.CornerRule != CornerRule.None)
        {
            var a = grid.Tile(from.X + dx, from.Y); var b = grid.Tile(from.X, from.Y + dy);
            if (settings.CornerRule == CornerRule.Official && (grid.TileVoid[a] || grid.TileVoid[b])) return new(StepReason.CornerVoid);
            var openA = CanFlank(actor, from, from.X + dx, from.Y);
            var openB = CanFlank(actor, from, from.X, from.Y + dy);
            if (settings.CornerRule == CornerRule.Strict ? !openA || !openB : !openA && !openB) return new(StepReason.CornerBlocked);
        }
        if ((flags & NavFlags.GuildGate) != 0 && !actor.IsMember(grid.GroupId[tile])) return new(StepReason.GateDenied);
        if (occupancy != null && (occupancy.Targets[tile] & ClaimMatrix.BlockingMask(actor, flags, purpose, view)) != 0) return new(StepReason.Occupied);
        return new(StepReason.Ok);
    }

    public bool CanFlank(ActorProfile actor, in NavPosition from, int x, int y)
    {
        if (!grid.InBounds(x, y)) return false;
        var t = grid.Tile(x, y); var flags = grid.Flags[t];
        return (flags & NavFlags.Transit) != 0 && (flags & NavFlags.FloorLocked) == 0
            && ((flags & NavFlags.GuildGate) == 0 || actor.IsMember(grid.GroupId[t]))
            && (actor.IgnoreStepHeight || HeightReason(grid.WalkZ[t] - from.Z) == StepReason.Ok);
    }

    private StepReason HeightReason(double dz) => dz > _maxUp ? StepReason.TooHigh
        : _maxDown is { } down && -dz > down ? StepReason.TooLow : StepReason.Ok;
}
