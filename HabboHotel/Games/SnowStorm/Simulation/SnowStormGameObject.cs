namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// A synchronized arena object (AIR <c>SnowWarGameObject</c>). Its variable vector (type, id, then type-specific values)
/// is both the wire layout and the checksum input. Type ids: 1 snowball, 2 tree, 3 pile, 4 machine, 5 human.
/// </summary>
public abstract class SnowStormGameObject(int id)
{
    public const int TypeSnowball = 1;
    public const int TypeTree = 2;
    public const int TypePile = 3;
    public const int TypeMachine = 4;
    public const int TypeHuman = 5;

    public int Id { get; } = id;

    public abstract int Type { get; }

    public bool IsActive { get; internal set; }

    public abstract int VariableCount { get; }

    public abstract int GetVariable(int index);

    public int[] GetVariables()
    {
        var variables = new int[VariableCount];

        for (var index = 0; index < variables.Length; index++) {
            variables[index] = GetVariable(index);
        }

        return variables;
    }

    internal abstract int LocationX { get; }

    internal abstract int LocationY { get; }

    internal abstract int BoundingRadius { get; }

    internal virtual int CollisionHeight => BoundingRadius;

    internal virtual void Subturn(SnowStormArena arena) { }

    internal virtual void OnRemove() { }

    // AIR SnowWarGameObject.testSnowBallCollision: below the collision height and strictly inside both radii.
    internal virtual bool TestSnowballCollision(SnowStormSnowball snowball) =>
        snowball.Z < CollisionHeight
        && SnowStormMath.IsInDistance(LocationX, LocationY, snowball.X, snowball.Y, BoundingRadius + SnowStormSnowball.Radius);

    internal virtual void OnSnowballHit(SnowStormArena arena, SnowStormSnowball snowball) { }
}
