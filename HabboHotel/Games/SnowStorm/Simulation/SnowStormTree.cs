namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// A tree (AIR <c>TreeGameObject</c>): absorbs snowballs until <see cref="MaxHits"/>. Variables: type, id, x, y,
/// direction, height, fuseObjectId, maxHits, hits.
/// </summary>
public sealed class SnowStormTree : SnowStormGameObject
{
    public const int DefaultMaxHits = 3;
    private const int AliveRadius = 3200 - SnowStormSnowball.Radius - 1;

    private readonly SnowStormTile _tile;

    internal SnowStormTree(SnowStormArena arena, int[] variables)
        : base(variables[1])
    {
        IsActive = true;
        _tile = arena.GetTile(SnowStormMath.WorldToTile(variables[2]), SnowStormMath.WorldToTile(variables[3]))
            ?? throw new ArgumentException($"Tree {Id} is outside the arena.");
        Direction = variables[4] is >= 0 and <= 7
            ? variables[4]
            : throw new ArgumentException($"Tree {Id} direction {variables[4]} is not 0..7.");
        Height = variables[5];
        FuseObjectId = variables[6];
        MaxHits = variables[7];
        Hits = variables[8];

        if (Hits < MaxHits) {
            _tile.AddGameObject(this);
        }

        _tile.AddToHeight(-Height);
        _tile.Blocked = true;
    }

    public override int Type => TypeTree;

    public override int VariableCount => 9;

    public int Direction { get; }

    public int Height { get; }

    public int FuseObjectId { get; }

    public int MaxHits { get; }

    public int Hits { get; private set; }

    public override int GetVariable(int index) => index switch
    {
        0 => TypeTree,
        1 => Id,
        2 => _tile.WorldX,
        3 => _tile.WorldY,
        4 => Direction,
        5 => Height,
        6 => FuseObjectId,
        7 => MaxHits,
        8 => Hits,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal override int LocationX => _tile.WorldX;

    internal override int LocationY => _tile.WorldY;

    internal override int BoundingRadius => Hits < MaxHits ? AliveRadius : 0;

    internal override int CollisionHeight => Height;

    internal override void OnSnowballHit(SnowStormArena arena, SnowStormSnowball snowball)
    {
        if (Hits < MaxHits) {
            Hits++;
        }

        if (Hits >= MaxHits) {
            _tile.RemoveGameObject();
        }
    }
}
