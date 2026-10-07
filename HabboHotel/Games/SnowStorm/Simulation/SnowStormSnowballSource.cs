namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>A machine or pile humans take snowballs from (AIR <c>SnowballGivingGameObject</c>).</summary>
public abstract class SnowStormSnowballSource : SnowStormGameObject
{
    private protected SnowStormSnowballSource(SnowStormArena arena, int[] variables, int snowballCount, int fuseObjectId)
        : base(variables[1])
    {
        IsActive = true;
        Tile = arena.GetTile(SnowStormMath.WorldToTile(variables[2]), SnowStormMath.WorldToTile(variables[3]))
            ?? throw new ArgumentException($"Snowball source {Id} is outside the arena.");
        SnowballCount = snowballCount;
        FuseObjectId = fuseObjectId;
    }

    public int SnowballCount { get; private protected set; }

    public abstract int MaxSnowballs { get; }

    public int FuseObjectId { get; }

    public int TileX => Tile.X;

    public int TileY => Tile.Y;

    private protected SnowStormTile Tile { get; }

    internal override int LocationX => Tile.WorldX;

    internal override int LocationY => Tile.WorldY;

    internal int PickupSnowballs(int count)
    {
        if (SnowballCount < count) {
            count = SnowballCount;
        }

        SnowballCount -= count;
        OnSnowballPickup();

        return count;
    }

    private protected virtual void OnSnowballPickup() { }
}

/// <summary>
/// A snowball machine (AIR <c>SnowballMachineGameObject</c>). Variables: type, id, x, y, direction, maxSnowballs,
/// snowballCount, fuseObjectId.
/// </summary>
public sealed class SnowStormMachine : SnowStormSnowballSource
{
    public const int DefaultMaxSnowballs = 5;
    private const int BoundingDataRadius = 1200;

    internal SnowStormMachine(SnowStormArena arena, int[] variables)
        : base(arena, variables, variables[6], variables[7])
    {
        Direction = variables[4] is >= 0 and <= 7
            ? variables[4]
            : throw new ArgumentException($"Machine {Id} direction {variables[4]} is not 0..7.");
        MaxSnowballs = variables[5];
        Tile.AddGameObject(this);
    }

    public override int Type => TypeMachine;

    public override int VariableCount => 8;

    public int Direction { get; }

    public override int MaxSnowballs { get; }

    public override int GetVariable(int index) => index switch
    {
        0 => TypeMachine,
        1 => Id,
        2 => Tile.WorldX,
        3 => Tile.WorldY,
        4 => Direction,
        5 => MaxSnowballs,
        6 => SnowballCount,
        7 => FuseObjectId,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal override int BoundingRadius => BoundingDataRadius;

    internal void CreateSnowball()
    {
        if (SnowballCount < MaxSnowballs) {
            SnowballCount++;
        }
    }
}

/// <summary>
/// A finite snowball pile (AIR <c>SnowballPileGameObject</c>); it stops colliding once empty. Variables: type, id, x,
/// y, maxSnowballs, snowballCount, fuseObjectId.
/// </summary>
public sealed class SnowStormPile : SnowStormSnowballSource
{
    public const int DefaultMaxSnowballs = 12;
    private const int RadiusPerSnowball = 100;

    private int _boundingRadius;

    internal SnowStormPile(SnowStormArena arena, int[] variables)
        : base(arena, variables, variables[5], variables[6])
    {
        MaxSnowballs = variables[4];

        if (SnowballCount > 0) {
            Tile.AddGameObject(this);
        }

        _boundingRadius = SnowballCount * RadiusPerSnowball;
    }

    public override int Type => TypePile;

    public override int VariableCount => 7;

    public override int MaxSnowballs { get; }

    public override int GetVariable(int index) => index switch
    {
        0 => TypePile,
        1 => Id,
        2 => Tile.WorldX,
        3 => Tile.WorldY,
        4 => MaxSnowballs,
        5 => SnowballCount,
        6 => FuseObjectId,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal override int BoundingRadius => _boundingRadius;

    private protected override void OnSnowballPickup()
    {
        _boundingRadius = SnowballCount * RadiusPerSnowball;

        if (SnowballCount <= 0) {
            Tile.RemoveGameObject();
        }
    }
}
