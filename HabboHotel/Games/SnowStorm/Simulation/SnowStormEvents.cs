namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// A GameStatus event (AIR <c>SnowWarGameEventData</c>). <see cref="Fields"/> are the ints that follow the event type on
/// the wire, in order. Only types 1, 2, 3, 4, 7, 8, 11 and 12 exist.
/// </summary>
public abstract record SnowStormEvent(int Type)
{
    public abstract int[] Fields { get; }
}

/// <summary>Type 1: the human is removed at the end of the subturn.</summary>
public sealed record SnowStormHumanLeftGame(int HumanId) : SnowStormEvent(1)
{
    public override int[] Fields => [HumanId];
}

/// <summary>Type 2: new move target in world units (tile * 3200).</summary>
public sealed record SnowStormNewMoveTarget(int HumanId, int X, int Y) : SnowStormEvent(2)
{
    public override int[] Fields => [HumanId, X, Y];
}

/// <summary>Type 3: the thrower turns to the target's current location; must be followed by <see cref="SnowStormCreateSnowball"/>.</summary>
public sealed record SnowStormThrowAtHuman(int HumanId, int TargetHumanId, int Trajectory) : SnowStormEvent(3)
{
    public override int[] Fields => [HumanId, TargetHumanId, Trajectory];
}

/// <summary>Type 4: like <see cref="SnowStormThrowAtHuman"/> with a world position target.</summary>
public sealed record SnowStormThrowAtPosition(int HumanId, int X, int Y, int Trajectory) : SnowStormEvent(4)
{
    public override int[] Fields => [HumanId, X, Y, Trajectory];
}

/// <summary>Type 7: the human starts making a snowball (20 subturns).</summary>
public sealed record SnowStormStartMakingSnowball(int HumanId) : SnowStormEvent(7)
{
    public override int[] Fields => [HumanId];
}

/// <summary>Type 8: spawns snowball <see cref="SnowballId"/> at the thrower's current location.</summary>
public sealed record SnowStormCreateSnowball(int SnowballId, int HumanId, int TargetX, int TargetY, int Trajectory) : SnowStormEvent(8)
{
    public override int[] Fields => [SnowballId, HumanId, TargetX, TargetY, Trajectory];
}

/// <summary>Type 11: machine snowball count + 1 (up to its maximum).</summary>
public sealed record SnowStormMachineCreatesSnowball(int MachineId) : SnowStormEvent(11)
{
    public override int[] Fields => [MachineId];
}

/// <summary>Type 12: the human takes one snowball from a machine or pile if it has capacity.</summary>
public sealed record SnowStormHumanGetsSnowball(int HumanId, int SourceId) : SnowStormEvent(12)
{
    public override int[] Fields => [HumanId, SourceId];
}

/// <summary>An event queued at (<see cref="Turn"/>, <see cref="Subturn"/>), subturn 0..2.</summary>
public sealed record SnowStormScheduledEvent(int Turn, int Subturn, SnowStormEvent Event);
