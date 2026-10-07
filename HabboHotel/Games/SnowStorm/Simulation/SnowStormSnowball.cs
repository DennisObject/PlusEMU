namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// A flying snowball (AIR <c>SnowBallGameObject</c>). Variables: type, id, x, y, z, movementDirection360, trajectory,
/// timeToLive, throwingHumanId, parabolaOffset, planarVelocity.
/// </summary>
public sealed class SnowStormSnowball : SnowStormGameObject
{
    public const int TrajectoryQuickThrow = 0;
    public const int TrajectoryShortLob = 1;
    public const int TrajectoryLongLob = 2;
    public const int TrajectoryDefaultThrow = 3;
    public const int ThrowVelocity = 2000;
    public const int InitialHeight = 3000;
    public const double LongLobTimeToTargetCoefficient = 0.0007072135785007072;
    public const double ShortLobTimeToTargetCoefficient = 0.000559;
    public const int ShortLobMaxRange = 60000;
    public const int LongLobMaxRange = 100000;
    public const int DefaultThrowToLobCutoffRange = 42000;
    public const int Radius = 400;

    internal SnowStormSnowball(int id)
        : base(id) { }

    public override int Type => TypeSnowball;

    public override int VariableCount => 11;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Z { get; private set; }

    public int Direction360 { get; private set; }

    public int Trajectory { get; private set; }

    public int TimeToLive { get; private set; }

    public int ParabolaOffset { get; private set; }

    public int PlanarVelocity { get; private set; }

    public SnowStormHuman? Thrower { get; private set; }

    public override int GetVariable(int index) => index switch
    {
        0 => TypeSnowball,
        1 => Id,
        2 => X,
        3 => Y,
        4 => Z,
        5 => Direction360,
        6 => Trajectory,
        7 => TimeToLive,
        8 => Thrower?.Id ?? 0,
        9 => ParabolaOffset,
        10 => PlanarVelocity,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal override int LocationX => X;

    internal override int LocationY => Y;

    internal override int BoundingRadius => Radius;

    // AIR SnowBallGameObject.initialize: distances are measured in 200-unit steps and clamped, never rejected.
    internal void Initialize(int x, int y, int z, int trajectory, int targetX, int targetY, SnowStormHuman thrower)
    {
        IsActive = true;
        X = x;
        Y = y;
        Z = z;
        int deltaX = (targetX - x) / 200;
        int deltaY = (targetY - y) / 200;
        Direction360 = SnowStormMath.ValidateDirection360(SnowStormMath.GetAngleFromComponents(deltaX, deltaY));
        int distance = SnowStormMath.FastSqrt(deltaX * deltaX + deltaY * deltaY) * 200;
        Trajectory = trajectory != TrajectoryDefaultThrow
            ? trajectory
            : distance <= DefaultThrowToLobCutoffRange ? TrajectoryQuickThrow
            : distance <= ShortLobMaxRange ? TrajectoryShortLob
            : TrajectoryLongLob;

        if (Trajectory == TrajectoryQuickThrow) {
            TimeToLive = 10;
            PlanarVelocity = ThrowVelocity;
        }
        else if (Trajectory == TrajectoryShortLob) {
            distance = Math.Min(distance, ShortLobMaxRange);
            TimeToLive = (int)(distance * ShortLobTimeToTargetCoefficient);
            PlanarVelocity = TimeToLive == 0 ? 0 : distance / TimeToLive;
        }
        else if (Trajectory == TrajectoryLongLob) {
            distance = Math.Min(distance, LongLobMaxRange);
            TimeToLive = (int)(distance * LongLobTimeToTargetCoefficient);
            PlanarVelocity = TimeToLive == 0 ? 0 : distance / TimeToLive;
        }

        ParabolaOffset = TimeToLive / 2;
        Thrower = thrower;
    }

    internal void InitializeFromVariables(int[] variables, SnowStormHuman? thrower)
    {
        IsActive = true;
        X = variables[2];
        Y = variables[3];
        Z = variables[4];
        Direction360 = SnowStormMath.ValidateDirection360(variables[5]);
        Trajectory = variables[6];
        TimeToLive = variables[7];
        Thrower = thrower;
        ParabolaOffset = variables[9];
        PlanarVelocity = variables[10];
    }

    internal override void Subturn(SnowStormArena arena)
    {
        if (!IsActive) {
            return;
        }

        TimeToLive--;

        if (Trajectory == TrajectoryQuickThrow) {
            UpdatePosition(10, true);
        }
        else if (Trajectory == TrajectoryShortLob) {
            UpdatePosition(25, false);
        }
        else {
            UpdatePosition(50, false);
        }

        var tile = arena.GetTile(SnowStormMath.WorldToTile(X), SnowStormMath.WorldToTile(Y));

        if (TestCollisions(arena, tile) || arena.TestCollisionWithGround(X, Y, Z)) {
            arena.PutGameObjectOnDeleteList(this);
        }
    }

    // Object on the ball's tile first, then the tiles ahead at dir8, dir8 - 45 and dir8 + 45 degrees.
    private bool TestCollisions(SnowStormArena arena, SnowStormTile? tile)
    {
        if (tile == null) {
            return false;
        }

        int direction8 = SnowStormMath.Direction360ToDirection8(Direction360);

        return TestCollision(arena, tile)
            || TestCollision(arena, tile.GetTileInDirection(direction8))
            || TestCollision(arena, tile.GetTileInDirection(SnowStormMath.RotateDirection8(direction8, -1)))
            || TestCollision(arena, tile.GetTileInDirection(SnowStormMath.RotateDirection8(direction8, 1)));
    }

    private bool TestCollision(SnowStormArena arena, SnowStormTile? tile)
    {
        if (tile?.GameObject is not { } gameObject || !gameObject.TestSnowballCollision(this)) {
            return false;
        }

        gameObject.OnSnowballHit(arena, this);

        return true;
    }

    private void UpdatePosition(int heightFactor, bool capHeight)
    {
        X += SnowStormMath.BaseVectorXComponent(Direction360) * PlanarVelocity / 255;
        Y += SnowStormMath.BaseVectorYComponent(Direction360) * PlanarVelocity / 255;
        int fromPeak = TimeToLive - ParabolaOffset;
        int z = (ParabolaOffset * ParabolaOffset - fromPeak * fromPeak) * heightFactor + InitialHeight;
        Z = capHeight ? Math.Min(z, InitialHeight) : z;
    }
}
