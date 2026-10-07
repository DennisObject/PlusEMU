namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// A player avatar (AIR <c>HumanGameObject</c>). Variables: type, id, x, y, currentTileX, currentTileY, bodyDirection,
/// hitPoints, snowballCount, isBot (always 0), activityTimer, activityState, nextTileX, nextTileY, moveTargetX,
/// moveTargetY, score, team, userId; strings name, mission, figure, sex.
/// </summary>
public sealed class SnowStormHuman : SnowStormGameObject
{
    public const int MoveSpeed = 534;
    public const int InitialSnowballCount = 5;
    public const int MaximumSnowballCount = 5;
    public const int InitialHitPoints = 5;
    public const int SnowballCreateTime = 20;
    public const int StunTime = 100;
    public const int InvincibleAfterStunTime = 60;
    public const int SnowballThrowInterval = 5;
    public const int StateNormal = 0;
    public const int StateMakingSnowball = 1;
    public const int StateStunned = 2;
    public const int StateInvincible = 3;
    private const int ScoreOnHit = 1;
    private const int ScoreOnKnockDown = 5;
    private const int BoundingDataRadius = 1600;
    private const int PlayerHeight = 5000;

    private SnowStormTile _currentTile;
    private SnowStormTile? _nextTile;

    internal SnowStormHuman(SnowStormArena arena, int[] variables, string[] strings)
        : base(variables[1])
    {
        X = variables[2];
        Y = variables[3];
        _currentTile = arena.GetTile(variables[4], variables[5])
            ?? throw new ArgumentException($"Human {Id} current tile ({variables[4]},{variables[5]}) does not exist.");
        BodyDirection = variables[6] is >= 0 and <= 7
            ? variables[6]
            : throw new ArgumentException($"Human {Id} body direction {variables[6]} is not 0..7.");
        HitPoints = variables[7];
        SnowballCount = variables[8];
        ActivityTimer = variables[10];
        ActivityState = variables[11];
        MoveTargetX = variables[14];
        MoveTargetY = variables[15];
        Score = variables[16];
        Team = variables[17];
        UserId = variables[18];
        Name = strings[0];
        Mission = strings[1];
        Figure = strings[2];
        Sex = strings[3];

        _currentTile.AddGameObject(this);
        var nextTile = arena.GetTile(variables[12], variables[13])
            ?? throw new ArgumentException($"Human {Id} next tile ({variables[12]},{variables[13]}) does not exist.");

        if (nextTile != _currentTile) {
            _nextTile = nextTile;
            _nextTile.AddGameObject(this);
            _currentTile.RemoveOccupyingHuman();
            IsMoving = true;
        }
    }

    public override int Type => TypeHuman;

    public override int VariableCount => 19;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int CurrentTileX => _currentTile.X;

    public int CurrentTileY => _currentTile.Y;

    public int NextTileX => (_nextTile ?? _currentTile).X;

    public int NextTileY => (_nextTile ?? _currentTile).Y;

    public bool HasNextTile => _nextTile != null;

    public int BodyDirection { get; private set; }

    public int HitPoints { get; private set; }

    public int SnowballCount { get; private set; }

    public int ActivityTimer { get; private set; }

    public int ActivityState { get; private set; }

    public int MoveTargetX { get; private set; }

    public int MoveTargetY { get; private set; }

    public int Score { get; private set; }

    public int Team { get; }

    public int UserId { get; }

    public string Name { get; }

    public string Mission { get; }

    public string Figure { get; }

    public string Sex { get; }

    /// <summary>Subturns until the next throw is allowed; not part of the variable vector (AIR resets it on full status).</summary>
    public int ThrowTimer { get; private set; }

    /// <summary>True while stepping between tiles (AIR posture <c>swrun</c>).</summary>
    public bool IsMoving { get; private set; }

    public bool IsStunned => ActivityState == StateStunned;

    public bool IsInvincible => ActivityState == StateInvincible;

    public bool CanMove => ActivityState is StateNormal or StateInvincible;

    public bool CanThrowSnowballs => SnowballCount > 0 && ThrowTimer < 1 && CanMove;

    public bool CanMakeSnowballs => CanMove && SnowballCount < MaximumSnowballCount;

    public int RemainingSnowballCapacity => MaximumSnowballCount - SnowballCount;

    public override int GetVariable(int index) => index switch
    {
        0 => TypeHuman,
        1 => Id,
        2 => X,
        3 => Y,
        4 => _currentTile.X,
        5 => _currentTile.Y,
        6 => BodyDirection,
        7 => HitPoints,
        8 => SnowballCount,
        9 => 0,
        10 => ActivityTimer,
        11 => ActivityState,
        12 => NextTileX,
        13 => NextTileY,
        14 => MoveTargetX,
        15 => MoveTargetY,
        16 => Score,
        17 => Team,
        18 => UserId,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal string[] GetStrings() => [Name, Mission, Figure, Sex];

    internal override int LocationX => X;

    internal override int LocationY => Y;

    internal override int BoundingRadius => BoundingDataRadius;

    internal override int CollisionHeight => PlayerHeight;

    internal override void OnRemove()
    {
        if (_currentTile.OccupyingHuman == this) {
            _currentTile.RemoveOccupyingHuman();
        }

        if (_nextTile?.OccupyingHuman == this) {
            _nextTile.RemoveOccupyingHuman();
        }

        IsMoving = false;
    }

    internal override void Subturn(SnowStormArena arena)
    {
        if (ActivityTimer > 0) {
            if (ActivityTimer == 1) {
                ActivityTimerTriggered(arena);
            }

            ActivityTimer--;
        }

        if (ThrowTimer > 0) {
            ThrowTimer--;
        }

        if (!CanMove) {
            IsMoving = false;

            return;
        }

        if (_nextTile != null) {
            MoveTowardsNextTile();

            return;
        }

        if (_currentTile.LocationIsInTileRange(MoveTargetX, MoveTargetY)) {
            IsMoving = false;

            return;
        }

        // AIR HumanGameObject.subturn: try the direction to the target, then -45 and +45 degrees; never path-find.
        int direction = SnowStormMath.Direction360ToDirection8(
            SnowStormMath.GetAngleFromComponents(MoveTargetX - _currentTile.WorldX, MoveTargetY - _currentTile.WorldY));
        _nextTile = _currentTile.GetTileInDirection(direction);

        if (_nextTile == null || !_nextTile.CanMoveTo()) {
            if (_nextTile != null && _nextTile.IsLocation(MoveTargetX, MoveTargetY)) {
                _nextTile = null;
                StopMovement();

                return;
            }

            direction = SnowStormMath.RotateDirection8(direction, -1);
            _nextTile = _currentTile.GetTileInDirection(direction);

            if (_nextTile == null || !_nextTile.CanMoveTo()) {
                direction = SnowStormMath.RotateDirection8(direction, 2);
                _nextTile = _currentTile.GetTileInDirection(direction);

                if (_nextTile != null && !_nextTile.CanMoveTo()) {
                    _nextTile = null;
                }
            }
        }

        if (_nextTile == null) {
            IsMoving = false;

            return;
        }

        _currentTile.RemoveOccupyingHuman();
        _nextTile.AddGameObject(this);
        BodyDirection = direction;
        MoveTowardsNextTile();
    }

    internal void ChangeMoveTarget(int x, int y)
    {
        if (ActivityState == StateMakingSnowball) {
            ActivityState = StateNormal;
            ActivityTimer = 0;
        }

        if (CanMove) {
            MoveTargetX = x;
            MoveTargetY = y;
        }
    }

    internal bool ThrowSnowball(SnowStormArena arena, int targetX, int targetY)
    {
        if (SnowballCount < 1) {
            return false;
        }

        StopMovement();
        BodyDirection = SnowStormMath.Direction360ToDirection8(SnowStormMath.GetAngleFromComponents(targetX - X, targetY - Y));
        SnowballCount--;
        arena.StatsFor(this).SnowballsThrown++;

        return true;
    }

    internal void StartThrowTimer() => ThrowTimer = SnowballThrowInterval;

    // Ray gun burst (Plus extra): face the gun's direction and take the throw posture; no ammo is spent.
    internal void FireRayGun(int direction8)
    {
        BodyDirection = direction8;
        StartThrowTimer();
    }

    internal void StartMakingSnowball()
    {
        if (!CanMakeSnowballs) {
            return;
        }

        ActivityState = StateMakingSnowball;
        ActivityTimer = SnowballCreateTime;
        StopMovement();
    }

    internal void AddSnowballs(int count) => SnowballCount += count;

    internal override void OnSnowballHit(SnowStormArena arena, SnowStormSnowball snowball)
    {
        // A ball restored from a full status whose thrower has left has no owner; AIR would throw here.
        if (snowball.Thrower is not { } thrower) {
            return;
        }

        PlayerIsHitBySnowball(arena, thrower, snowball.Direction360);
        thrower.OnHitHuman(arena, this);
    }

    internal override bool TestSnowballCollision(SnowStormSnowball snowball) =>
        ActivityState != StateStunned
        && ActivityState != StateInvincible
        && snowball.Thrower != this
        && base.TestSnowballCollision(snowball);

    private void PlayerIsHitBySnowball(SnowStormArena arena, SnowStormHuman thrower, int direction360)
    {
        if (Team == thrower.Team) {
            arena.StatsFor(thrower).FriendlyHits++;

            return;
        }

        if (HitPoints <= 0) {
            return;
        }

        if (HitPoints == 1) {
            PlayerFallsDown(direction360);
            thrower.OnKnockDownHuman(arena, this);
            arena.StatsFor(thrower).Kills++;
            arena.StatsFor(this).Deaths++;
        }

        HitPoints--;
        arena.StatsFor(thrower).SnowballHits++;
        arena.StatsFor(this).SnowballHitsTaken++;
    }

    private void OnHitHuman(SnowStormArena arena, SnowStormHuman victim)
    {
        if (Team != victim.Team || arena.IsDeathMatch) {
            AddScore(arena, ScoreOnHit);
        }
    }

    private void OnKnockDownHuman(SnowStormArena arena, SnowStormHuman victim)
    {
        if (Team != victim.Team || arena.IsDeathMatch) {
            AddScore(arena, ScoreOnKnockDown);
        }
    }

    private void AddScore(SnowStormArena arena, int score)
    {
        Score += score;
        arena.AddTeamScore(Team, score);
    }

    private void PlayerFallsDown(int direction360)
    {
        ActivityState = StateStunned;
        ActivityTimer = StunTime;
        BodyDirection = SnowStormMath.RotateDirection8(SnowStormMath.Direction360ToDirection8(direction360), 4);
        StopMovement();
    }

    private void ActivityTimerTriggered(SnowStormArena arena)
    {
        if (ActivityState == StateStunned) {
            HitPoints = InitialHitPoints;
            ActivityState = StateInvincible;
            ActivityTimer = InvincibleAfterStunTime;

            return;
        }

        if (ActivityState == StateMakingSnowball) {
            SnowballCount++;
            arena.StatsFor(this).SnowballsCreated++;
        }

        ActivityState = StateNormal;
    }

    // AIR stopMovement: snap to the next tile centre when stepping, otherwise to the current tile centre.
    private void StopMovement()
    {
        if (_nextTile != null) {
            _currentTile = _nextTile;
            _nextTile = null;
        }

        X = _currentTile.WorldX;
        Y = _currentTile.WorldY;
        MoveTargetX = _currentTile.WorldX;
        MoveTargetY = _currentTile.WorldY;
        IsMoving = false;
    }

    private void MoveTowardsNextTile()
    {
        var nextTile = _nextTile!;
        X = StepAxis(X, nextTile.WorldX);
        Y = StepAxis(Y, nextTile.WorldY);

        if (SnowStormMath.Abs(nextTile.WorldX - X) + SnowStormMath.Abs(nextTile.WorldY - Y) < 267) {
            _currentTile = nextTile;
            _nextTile = null;
        }

        IsMoving = true;
    }

    private static int StepAxis(int current, int target)
    {
        int delta = current - target;

        if (delta == 0) {
            return current;
        }

        if (delta < 0) {
            return delta > -MoveSpeed ? target : current + MoveSpeed;
        }

        return delta < MoveSpeed ? target : current - MoveSpeed;
    }
}
