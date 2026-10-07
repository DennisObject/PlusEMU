namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// The deterministic AIR SnowStorm simulation (<c>SynchronizedGameArena</c> + <c>class_2526</c>/<c>class_2527</c> stage):
/// tiles from <see cref="SnowStormLevelData"/>, game objects in insertion order, input events queued per
/// (turn, subturn) and a checksum per turn. Not thread-safe; drive it from one tick.
/// <para>
/// Turn loop (server): <see cref="RunTurn"/> simulates <see cref="Turn"/> (apply its events, 3 subturns, checksum),
/// then schedule the inputs for the next turn and broadcast GameStatus(result.Turn, result.Checksum,
/// <see cref="GetScheduledEvents"/>(result.Turn + 1)).
/// </para>
/// </summary>
public sealed class SnowStormArena
{
    public const int SubturnsPerTurn = 3;
    public const int SubturnMilliseconds = 50;

    private readonly SnowStormTile?[,] _tiles;
    private readonly List<SnowStormGameObject> _objects = [];
    private readonly Dictionary<int, SnowStormGameObject> _objectsById = [];
    private readonly List<SnowStormGameObject> _deleteList = [];
    private readonly Dictionary<int, List<SnowStormEvent>[]> _eventQueues = [];
    private readonly Dictionary<int, int> _checksums = [];
    private readonly Dictionary<int, SnowStormPlayerStats> _stats = [];
    private readonly int[] _teamScores;
    private int _nextObjectId = 1;
    private bool _skipObjectUpdates;

    private SnowStormArena(SnowStormLevelData level, int numberOfTeams)
    {
        Level = level;
        NumberOfTeams = numberOfTeams;
        _teamScores = new int[numberOfTeams];
        _tiles = new SnowStormTile?[level.Height, level.Width];
        RayGuns = level.FuseObjects.Select(SnowStormRayGun.From).OfType<SnowStormRayGun>().ToList();
        LinkTiles(level);

        foreach (var fuseObject in level.FuseObjects) {
            var tile = GetTile(fuseObject.X, fuseObject.Y);

            if (tile == null) {
                continue;
            }

            tile.AddFuseObject(fuseObject);
            AdjustNeighbouringTiles(fuseObject);
        }
    }

    public SnowStormLevelData Level { get; }

    public int NumberOfTeams { get; }

    public bool IsDeathMatch => NumberOfTeams == 1;

    /// <summary>The next turn <see cref="RunTurn"/> will simulate.</summary>
    public int Turn { get; private set; }

    /// <summary>Team scores indexed by team - 1.</summary>
    public IReadOnlyList<int> TeamScores => _teamScores;

    public IEnumerable<SnowStormGameObject> Objects => _objects;

    public IEnumerable<SnowStormHuman> Humans => _objects.OfType<SnowStormHuman>();

    public IEnumerable<SnowStormMachine> Machines => _objects.OfType<SnowStormMachine>();

    public IEnumerable<SnowStormPile> Piles => _objects.OfType<SnowStormPile>();

    /// <summary>The level's ray guns (Plus extra), in fuse order.</summary>
    public IReadOnlyList<SnowStormRayGun> RayGuns { get; }

    private int Subturn { get; set; }

    /// <summary>
    /// Builds the arena and its level objects, in fuse order: trees (<c>snst_tree1</c>, <c>snst_tree1_d</c>, 3 hits,
    /// height = fuse height), piles (<c>snst_ballpile</c>, 12 of 12) and machines (<c>s_snowball_machine</c>, 0 of 5).
    /// Object ids start at 1. Add players with <see cref="AddHuman"/>.
    /// </summary>
    public static SnowStormArena Create(SnowStormLevelData level, int numberOfTeams)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numberOfTeams, 1);
        var arena = new SnowStormArena(level, numberOfTeams);

        foreach (var fuseObject in level.FuseObjects) {
            int x = SnowStormMath.TileToWorld(fuseObject.X);
            int y = SnowStormMath.TileToWorld(fuseObject.Y);
            int[]? variables = fuseObject.Name switch
            {
                "snst_tree1" or "snst_tree1_d" =>
                    [SnowStormGameObject.TypeTree, 0, x, y, fuseObject.Direction, fuseObject.Height, fuseObject.Id, SnowStormTree.DefaultMaxHits, 0],
                "snst_ballpile" =>
                    [SnowStormGameObject.TypePile, 0, x, y, SnowStormPile.DefaultMaxSnowballs, SnowStormPile.DefaultMaxSnowballs, fuseObject.Id],
                "s_snowball_machine" or "snowball_machine" =>
                    [SnowStormGameObject.TypeMachine, 0, x, y, fuseObject.Direction, SnowStormMachine.DefaultMaxSnowballs, 0, fuseObject.Id],
                _ => null
            };

            if (variables != null && arena.GetTile(fuseObject.X, fuseObject.Y) != null) {
                variables[1] = arena.AllocateObjectId();
                arena.AddGameObject(arena.CreateGameObject(new SnowStormObjectSnapshot(variables)));
            }
        }

        return arena;
    }

    /// <summary>Adds a player standing on a tile centre with full health and 5 snowballs; returns the new human.</summary>
    public SnowStormHuman AddHuman(SnowStormPlayer player, int tileX, int tileY, int bodyDirection)
    {
        int x = SnowStormMath.TileToWorld(tileX);
        int y = SnowStormMath.TileToWorld(tileY);
        var human = new SnowStormHuman(this,
            [
                SnowStormGameObject.TypeHuman, AllocateObjectId(), x, y, tileX, tileY, bodyDirection,
                SnowStormHuman.InitialHitPoints, SnowStormHuman.InitialSnowballCount, 0, 0, SnowStormHuman.StateNormal,
                tileX, tileY, x, y, 0, player.Team, player.UserId
            ],
            [player.Name, player.Mission, player.Figure, player.Sex]);
        AddGameObject(human);

        return human;
    }

    /// <summary>Reserves an unused object id, e.g. for the snowball of a CreateSnowball event.</summary>
    public int AllocateObjectId() => _nextObjectId++;

    /// <summary>Reserves <paramref name="count"/> consecutive object ids and returns the first.</summary>
    public int AllocateObjectIds(int count)
    {
        int first = _nextObjectId;
        _nextObjectId += count;

        return first;
    }

    public SnowStormGameObject? GetObject(int id) => _objectsById.GetValueOrDefault(id);

    public SnowStormPlayerStats GetStats(int humanId) => _stats.GetValueOrDefault(humanId) ?? new SnowStormPlayerStats();

    public int? GetChecksum(int turn) => _checksums.TryGetValue(turn, out int checksum) ? checksum : null;

    public bool IsWalkable(int tileX, int tileY) => GetTile(tileX, tileY)?.CanMoveTo() == true;

    /// <summary>Queues an event at (turn, subturn); turn must not be before <see cref="Turn"/>. Same-slot events apply in order.</summary>
    public void Schedule(int turn, int subturn, SnowStormEvent gameEvent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(turn, Turn);
        ArgumentOutOfRangeException.ThrowIfNegative(subturn);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(subturn, SubturnsPerTurn);

        if (!_eventQueues.TryGetValue(turn, out var queue)) {
            queue = [[], [], []];
            _eventQueues[turn] = queue;
        }

        queue[subturn].Add(gameEvent);
    }

    public IReadOnlyList<SnowStormScheduledEvent> GetScheduledEvents(int turn)
    {
        if (!_eventQueues.TryGetValue(turn, out var queue)) {
            return [];
        }

        var events = new List<SnowStormScheduledEvent>();

        for (var subturn = 0; subturn < SubturnsPerTurn; subturn++) {
            events.AddRange(queue[subturn].Select(gameEvent => new SnowStormScheduledEvent(turn, subturn, gameEvent)));
        }

        return events;
    }

    /// <summary>
    /// Simulates <see cref="Turn"/>: per subturn its queued events, then every object, then deletions; the checksum is
    /// taken after the third subturn. Object references are resolved when the turn starts, like AIR resolves them when
    /// the GameStatus arrives; events naming a missing object are dropped.
    /// </summary>
    public SnowStormTurnResult RunTurn()
    {
        int turn = Turn;
        var applied = new List<SnowStormScheduledEvent>();
        var bound = new List<Action>[SubturnsPerTurn];
        _eventQueues.Remove(turn, out var queue);

        for (var subturn = 0; subturn < SubturnsPerTurn; subturn++) {
            bound[subturn] = [];

            foreach (var gameEvent in queue?[subturn] ?? []) {
                if (Bind(gameEvent) is { } action) {
                    bound[subturn].Add(action);
                    applied.Add(new SnowStormScheduledEvent(turn, subturn, gameEvent));
                }
            }
        }

        for (var subturn = 0; subturn < SubturnsPerTurn; subturn++) {
            GamePulse(bound[subturn]);
        }

        return new SnowStormTurnResult(turn, _checksums[turn], applied);
    }

    /// <summary>The object dump for StageStarting / FullGameStatus, in insertion (checksum) order.</summary>
    public IReadOnlyList<SnowStormObjectSnapshot> Snapshot() =>
        _objects.Where(gameObject => gameObject.IsActive)
            .Select(gameObject => new SnowStormObjectSnapshot(gameObject.GetVariables(), (gameObject as SnowStormHuman)?.GetStrings()))
            .ToList();

    /// <summary>
    /// AIR FullGameStatus: replaces every object with <paramref name="objects"/> and seeks to <paramref name="turn"/>,
    /// which then runs without object updates. Schedule the status' pending events at turn + 1 afterwards.
    /// </summary>
    public void ApplyFullStatus(IReadOnlyList<SnowStormObjectSnapshot> objects, int turn, int checksum)
    {
        foreach (var tile in _tiles) {
            tile?.RemoveGameObject();
        }

        foreach (var gameObject in _objects) {
            gameObject.OnRemove();
        }

        _objects.Clear();
        _objectsById.Clear();
        _deleteList.Clear();

        foreach (var snapshot in objects) {
            AddGameObject(CreateGameObject(snapshot));
            _nextObjectId = Math.Max(_nextObjectId, snapshot.Id + 1);
        }

        Turn = turn;
        Subturn = 0;
        _checksums[turn] = checksum;
        _eventQueues.Clear();
        _skipObjectUpdates = true;
    }

    public int CalculateChecksum(int turn)
    {
        int checksum = SnowStormMath.IterateSeed(turn);

        foreach (var gameObject in _objects) {
            if (!gameObject.IsActive) {
                continue;
            }

            for (var index = 0; index < gameObject.VariableCount; index++) {
                checksum += gameObject.GetVariable(index) * (index + 1);
            }
        }

        return checksum;
    }

    internal IEnumerable<SnowStormEvent> PendingEvents() => _eventQueues.Values.SelectMany(queue => queue.SelectMany(events => events));

    internal SnowStormTile? GetTile(int x, int y) =>
        x < 0 || y < 0 || x >= Level.Width || y >= Level.Height ? null : _tiles[y, x];

    internal SnowStormPlayerStats StatsFor(SnowStormHuman human)
    {
        if (!_stats.TryGetValue(human.Id, out var stats)) {
            stats = new SnowStormPlayerStats();
            _stats[human.Id] = stats;
        }

        return stats;
    }

    internal void AddTeamScore(int team, int score)
    {
        if (team > 0 && team <= NumberOfTeams) {
            _teamScores[team - 1] += score;
        }
    }

    internal void PutGameObjectOnDeleteList(SnowStormGameObject gameObject)
    {
        _deleteList.Add(gameObject);
        gameObject.IsActive = false;
    }

    // AIR class_2527.testCollisionWithGround: below 1, or below the summed fuse height of the ball's tile.
    internal bool TestCollisionWithGround(int x, int y, int z)
    {
        if (z < 1) {
            return true;
        }

        var tile = GetTile(SnowStormMath.WorldToTile(x), SnowStormMath.WorldToTile(y));

        return tile != null && z < tile.Height;
    }

    private void GamePulse(List<Action> events)
    {
        foreach (var gameEvent in events) {
            gameEvent();
        }

        if (!_skipObjectUpdates) {
            foreach (var gameObject in _objects.ToArray()) {
                gameObject.Subturn(this);
            }

            foreach (var gameObject in _deleteList) {
                RemoveGameObject(gameObject.Id);
            }

            _deleteList.Clear();
        }

        if (Subturn >= SubturnsPerTurn - 1) {
            _checksums[Turn] = CalculateChecksum(Turn);
            Turn++;
            _skipObjectUpdates = false;
        }

        Subturn = (Subturn + 1) % SubturnsPerTurn;
    }

    private Action? Bind(SnowStormEvent gameEvent)
    {
        switch (gameEvent) {
            case SnowStormHumanLeftGame e when GetObject(e.HumanId) is SnowStormHuman human:
                return () =>
                {
                    PutGameObjectOnDeleteList(human);
                    human.OnRemove();
                };
            case SnowStormNewMoveTarget e when GetObject(e.HumanId) is SnowStormHuman human:
                return () => human.ChangeMoveTarget(e.X, e.Y);
            case SnowStormThrowAtHuman e when GetObject(e.HumanId) is SnowStormHuman human
                && GetObject(e.TargetHumanId) is SnowStormHuman target:
                return () =>
                {
                    human.ThrowSnowball(this, target.X, target.Y);
                    human.StartThrowTimer();
                };
            case SnowStormThrowAtPosition e when GetObject(e.HumanId) is SnowStormHuman human:
                return () =>
                {
                    human.ThrowSnowball(this, e.X, e.Y);
                    human.StartThrowTimer();
                };
            case SnowStormStartMakingSnowball e when GetObject(e.HumanId) is SnowStormHuman human:
                return human.StartMakingSnowball;
            case SnowStormCreateSnowball e when GetObject(e.HumanId) is SnowStormHuman human:
                return () => AddSnowball(e.SnowballId, human, e.TargetX, e.TargetY, e.Trajectory);
            case SnowStormRayGunBurst e when GetObject(e.HumanId) is SnowStormHuman human
                && RayGuns.FirstOrDefault(gun => gun.FuseObjectId == e.RayGunFuseObjectId) is { } gun:
                return () =>
                {
                    human.FireRayGun(gun.Direction);
                    var targets = gun.BurstTargets();

                    for (var index = 0; index < targets.Count; index++) {
                        AddSnowball(e.FirstSnowballId + index, human, SnowStormMath.TileToWorld(targets[index].X),
                            SnowStormMath.TileToWorld(targets[index].Y), SnowStormSnowball.TrajectoryDefaultThrow);
                    }
                };
            case SnowStormMachineCreatesSnowball e when GetObject(e.MachineId) is SnowStormMachine machine:
                return machine.CreateSnowball;
            case SnowStormHumanGetsSnowball e when GetObject(e.HumanId) is SnowStormHuman human
                && GetObject(e.SourceId) is SnowStormSnowballSource source:
                return () =>
                {
                    if (human.RemainingSnowballCapacity <= 0) {
                        return;
                    }

                    int count = source.PickupSnowballs(1);

                    if (count > 0) {
                        human.AddSnowballs(count);
                        StatsFor(human).SnowballsFromMachine += count;
                    }
                };
            default:
                return null;
        }
    }

    // AIR CreateSnowballEvent: the ball starts at the thrower's current location; a duplicate id is ignored.
    private void AddSnowball(int id, SnowStormHuman thrower, int targetX, int targetY, int trajectory)
    {
        var snowball = new SnowStormSnowball(id);

        if (_objectsById.TryAdd(snowball.Id, snowball)) {
            _objects.Add(snowball);
            snowball.Initialize(thrower.X, thrower.Y, SnowStormSnowball.InitialHeight, trajectory, targetX, targetY, thrower);
            _nextObjectId = Math.Max(_nextObjectId, snowball.Id + 1);
        }
    }

    private SnowStormGameObject CreateGameObject(SnowStormObjectSnapshot snapshot)
    {
        switch (snapshot.Type) {
            case SnowStormGameObject.TypeSnowball:
                var snowball = new SnowStormSnowball(snapshot.Id);
                snowball.InitializeFromVariables(snapshot.Variables, GetObject(snapshot.Variables[8]) as SnowStormHuman);

                return snowball;
            case SnowStormGameObject.TypeTree:
                return new SnowStormTree(this, snapshot.Variables);
            case SnowStormGameObject.TypePile:
                return new SnowStormPile(this, snapshot.Variables);
            case SnowStormGameObject.TypeMachine:
                return new SnowStormMachine(this, snapshot.Variables);
            case SnowStormGameObject.TypeHuman:
                return new SnowStormHuman(this, snapshot.Variables, snapshot.Strings ?? ["", "", "", ""]);
            default:
                throw new ArgumentException($"Unknown SnowStorm object type {snapshot.Type}.");
        }
    }

    private void AddGameObject(SnowStormGameObject gameObject)
    {
        if (!_objectsById.TryAdd(gameObject.Id, gameObject)) {
            return;
        }

        _objects.Add(gameObject);
        gameObject.IsActive = true;
    }

    private void RemoveGameObject(int id)
    {
        if (!_objectsById.Remove(id, out var gameObject)) {
            return;
        }

        _objects.Remove(gameObject);
        gameObject.OnRemove();
    }

    // AIR class_2527.linkTiles: every char except lowercase 'x' (including a missing one) is a tile.
    private void LinkTiles(SnowStormLevelData level)
    {
        string[] rows = level.HeightMap.Split('\r');

        if (rows.Length < level.Height) {
            throw new ArgumentException($"Heightmap has {rows.Length} rows, level height is {level.Height}.");
        }

        for (var y = 0; y < level.Height; y++) {
            for (var x = 0; x < level.Width; x++) {
                if (x < rows[y].Length && rows[y][x] == 'x') {
                    continue;
                }

                var tile = new SnowStormTile(x, y);
                _tiles[y, x] = tile;
                LinkIfPresent(tile, x + 1, y - 1, 1);
                LinkIfPresent(tile, x, y - 1, 0);
                LinkIfPresent(tile, x - 1, y - 1, 7);
                LinkIfPresent(tile, x - 1, y, 6);
            }
        }
    }

    private void LinkIfPresent(SnowStormTile tile, int x, int y, int direction8)
    {
        if (GetTile(x, y) is { } neighbour) {
            tile.LinkTile(neighbour, direction8);
        }
    }

    // AIR class_2527.checkAndAdjustNeighbouringTiles: only the first row and first column of the footprint.
    private void AdjustNeighbouringTiles(SnowStormFuseObject fuseObject)
    {
        int xDimension = fuseObject.XDimension;
        int yDimension = fuseObject.YDimension;

        if (fuseObject.Direction is 2 or 6) {
            (xDimension, yDimension) = (yDimension, xDimension);
        }

        for (var offset = 1; offset < xDimension; offset++) {
            AdjustFootprintTile(GetTile(fuseObject.X + offset, fuseObject.Y), fuseObject);
        }

        for (var offset = 1; offset < yDimension; offset++) {
            AdjustFootprintTile(GetTile(fuseObject.X, fuseObject.Y + offset), fuseObject);
        }
    }

    private static void AdjustFootprintTile(SnowStormTile? tile, SnowStormFuseObject fuseObject)
    {
        if (tile == null) {
            return;
        }

        tile.AddToHeight(fuseObject.Height);

        if (!fuseObject.CanStandOn) {
            tile.Blocked = true;
        }
    }
}
