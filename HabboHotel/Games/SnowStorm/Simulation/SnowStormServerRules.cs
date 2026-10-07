using System.Diagnostics.CodeAnalysis;

namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// Server-only decisions the AIR client never makes (Polaris rules): input validation, machine refills (event 11),
/// snowball pickups (event 12), Plus ray gun bursts (event 100) and spawn placement. Everything is emitted as events so
/// both simulations stay in step. One instance per game; call <see cref="ScheduleRefillsAndPickups"/> (and
/// <see cref="ScheduleRayGunBursts"/> when ray guns are on) once per turn after <see cref="SnowStormArena.RunTurn"/>.
/// </summary>
public sealed class SnowStormServerRules(SnowStormArena arena)
{
    /// <summary>Generator reload value: one snowball every 101 subturns while below the machine maximum.</summary>
    public const int MachineRefillTimer = 100;

    /// <summary>Subturns between two pickups of the same human.</summary>
    public const int PickupInterval = 20;

    private const int CenterTile = 25;

    /// <summary>Subturns a ray gun needs between two bursts.</summary>
    public const int RayGunCooldown = 60;

    private readonly Dictionary<int, int> _machineTimers = [];
    private readonly Dictionary<int, int> _pickupTimers = [];
    private readonly Dictionary<int, int> _rayGunLastBurst = [];
    private readonly HashSet<int> _rayGunArrivals = [];

    /// <summary>AIR <c>class_2527.calculateDirectionTowardsCenter</c>: Direction8 from a tile towards tile (25, 25).</summary>
    public static int DirectionTowardsCenter(int tileX, int tileY) =>
        SnowStormMath.Direction360ToDirection8(SnowStormMath.GetAngleFromComponents(CenterTile - tileX, CenterTile - tileY));

    /// <summary>
    /// Picks one walkable spawn tile per player (facing the centre): a free tile from the player's team list when
    /// given, otherwise the Polaris spread rule (random walkable tile at least 12, 10, ... 0 tiles from earlier spawns).
    /// </summary>
    public IReadOnlyList<(int X, int Y, int BodyDirection)> ChooseSpawns(
        IReadOnlyList<int> playerTeams,
        IReadOnlyDictionary<int, IReadOnlyList<(int X, int Y)>>? teamSpawnTiles,
        Random random)
    {
        var chosen = new List<(int X, int Y)>();

        foreach (int team in playerTeams) {
            var teamTiles = teamSpawnTiles?.GetValueOrDefault(team)?
                .Where(tile => arena.IsWalkable(tile.X, tile.Y) && !chosen.Contains(tile))
                .ToList();
            chosen.Add(teamTiles is { Count: > 0 } ? teamTiles[random.Next(teamTiles.Count)] : SpreadSpawn(chosen, random));
        }

        return chosen.Select(tile => (tile.X, tile.Y, DirectionTowardsCenter(tile.X, tile.Y))).ToList();
    }

    /// <summary>Schedules a move target (world units, clamped to the arena).</summary>
    public bool TryScheduleMove(int turn, int subturn, int humanId, int x, int y)
    {
        if (arena.GetObject(humanId) is not SnowStormHuman) {
            return false;
        }

        arena.Schedule(turn, subturn, new SnowStormNewMoveTarget(humanId, ClampX(x), ClampY(y)));

        return true;
    }

    /// <summary>Schedules make-snowball if the human can make one and has none pending.</summary>
    public bool TryScheduleMakeSnowball(int turn, int subturn, int humanId)
    {
        if (arena.GetObject(humanId) is not SnowStormHuman { CanMakeSnowballs: true }
            || arena.PendingEvents().OfType<SnowStormStartMakingSnowball>().Any(pending => pending.HumanId == humanId)) {
            return false;
        }

        arena.Schedule(turn, subturn, new SnowStormStartMakingSnowball(humanId));

        return true;
    }

    /// <summary>
    /// Schedules a throw at an opponent as the AIR pair: event 3 then event 8 (new snowball id, aimed at the target's
    /// current location) in the same subturn.
    /// </summary>
    public bool TryScheduleThrowAtHuman(int turn, int subturn, int humanId, int targetHumanId, int trajectory)
    {
        if (!CanThrow(humanId, trajectory, out var human)
            || arena.GetObject(targetHumanId) is not SnowStormHuman target
            || (target.Team == human.Team && !arena.IsDeathMatch)) {
            return false;
        }

        arena.Schedule(turn, subturn, new SnowStormThrowAtHuman(humanId, targetHumanId, trajectory));
        arena.Schedule(turn, subturn, new SnowStormCreateSnowball(arena.AllocateObjectId(), humanId, target.X, target.Y, trajectory));

        return true;
    }

    /// <summary>Schedules a throw at a world position (clamped to the arena) as events 4 and 8 in the same subturn.</summary>
    public bool TryScheduleThrowAtPosition(int turn, int subturn, int humanId, int x, int y, int trajectory)
    {
        if (!CanThrow(humanId, trajectory, out _)) {
            return false;
        }

        x = ClampX(x);
        y = ClampY(y);
        arena.Schedule(turn, subturn, new SnowStormThrowAtPosition(humanId, x, y, trajectory));
        arena.Schedule(turn, subturn, new SnowStormCreateSnowball(arena.AllocateObjectId(), humanId, x, y, trajectory));

        return true;
    }

    /// <summary>
    /// Runs the Polaris machine generators and pickup timers for the three subturns of <see cref="SnowStormArena.Turn"/>
    /// against the current state and schedules the resulting events 11 and 12 there. Machines refill before pickups;
    /// a human standing still on a machine's pickup tile (x, y + 1) or next to a pile (Manhattan 1) takes one ball.
    /// </summary>
    public IReadOnlyList<SnowStormScheduledEvent> ScheduleRefillsAndPickups()
    {
        int turn = arena.Turn;
        var scheduled = new List<SnowStormScheduledEvent>();
        var machineAdds = new Dictionary<int, int>();
        var reserved = new Dictionary<int, int>();
        var received = new Dictionary<int, int>();
        var machines = arena.Machines.ToList();
        var piles = arena.Piles.ToList();
        var humans = arena.Humans.ToList();

        for (var subturn = 0; subturn < SnowStormArena.SubturnsPerTurn; subturn++) {
            foreach (var machine in machines) {
                int timer = _machineTimers.GetValueOrDefault(machine.Id, MachineRefillTimer);

                if (timer > 0) {
                    _machineTimers[machine.Id] = timer - 1;
                    continue;
                }

                _machineTimers[machine.Id] = MachineRefillTimer;

                if (machine.SnowballCount + machineAdds.GetValueOrDefault(machine.Id) < machine.MaxSnowballs) {
                    machineAdds[machine.Id] = machineAdds.GetValueOrDefault(machine.Id) + 1;
                    scheduled.Add(Schedule(turn, subturn, new SnowStormMachineCreatesSnowball(machine.Id)));
                }
            }

            foreach (var human in humans) {
                int timer = _pickupTimers.GetValueOrDefault(human.Id);

                if (timer > 0) {
                    _pickupTimers[human.Id] = --timer;
                }

                if (timer != 0 || human.HasNextTile || human.IsMoving || !human.CanMove
                    || human.SnowballCount + received.GetValueOrDefault(human.Id) >= SnowStormHuman.MaximumSnowballCount) {
                    continue;
                }

                SnowStormSnowballSource? source =
                    machines.FirstOrDefault(machine => machine.TileX == human.CurrentTileX && machine.TileY + 1 == human.CurrentTileY
                        && HasAvailable(machine, reserved))
                    ?? (SnowStormSnowballSource?)piles.FirstOrDefault(pile =>
                        Math.Abs(pile.TileX - human.CurrentTileX) + Math.Abs(pile.TileY - human.CurrentTileY) == 1
                        && HasAvailable(pile, reserved));

                if (source == null) {
                    continue;
                }

                reserved[source.Id] = reserved.GetValueOrDefault(source.Id) + 1;
                received[human.Id] = received.GetValueOrDefault(human.Id) + 1;
                _pickupTimers[human.Id] = PickupInterval;
                scheduled.Add(Schedule(turn, subturn, new SnowStormHumanGetsSnowball(human.Id, source.Id)));
            }
        }

        return scheduled;
    }

    /// <summary>
    /// Plus ray guns: a human whose walk ended on a gun's use tile fires it once per arrival (in state 0 or 3), unless
    /// that gun burst within the last <see cref="RayGunCooldown"/> subturns. Schedules event 100 at
    /// (<see cref="SnowStormArena.Turn"/>, 0) with 7 consecutive new snowball ids. Call after <see cref="SnowStormArena.RunTurn"/>.
    /// </summary>
    public IReadOnlyList<SnowStormScheduledEvent> ScheduleRayGunBursts()
    {
        int turn = arena.Turn;
        int subturn = turn * SnowStormArena.SubturnsPerTurn;
        var leaving = arena.PendingEvents().OfType<SnowStormHumanLeftGame>().Select(pending => pending.HumanId).ToHashSet();
        var scheduled = new List<SnowStormScheduledEvent>();

        foreach (var human in arena.Humans) {
            var gun = StandsStill(human)
                ? arena.RayGuns.FirstOrDefault(rayGun => rayGun.UseX == human.CurrentTileX && rayGun.UseY == human.CurrentTileY)
                : null;

            if (gun == null) {
                _rayGunArrivals.Remove(human.Id);
                continue;
            }

            // An arrival counts once the human may act (state 0 or 3), so a stun or snowball on the tile only delays it.
            if (!human.CanMove || !_rayGunArrivals.Add(human.Id) || leaving.Contains(human.Id)
                || _rayGunLastBurst.TryGetValue(gun.FuseObjectId, out int last) && subturn - last < RayGunCooldown) {
                continue;
            }

            _rayGunLastBurst[gun.FuseObjectId] = subturn;
            scheduled.Add(Schedule(turn, 0,
                new SnowStormRayGunBurst(human.Id, gun.FuseObjectId, arena.AllocateObjectIds(SnowStormRayGun.BurstSize))));
        }

        return scheduled;
    }

    // The walk ended here: on the tile centre, no next tile, and the move target inside this tile.
    private static bool StandsStill(SnowStormHuman human)
    {
        int x = SnowStormMath.TileToWorld(human.CurrentTileX);
        int y = SnowStormMath.TileToWorld(human.CurrentTileY);

        return !human.HasNextTile && human.X == x && human.Y == y
            && SnowStormMath.Abs(human.MoveTargetX - x) < SnowStormMath.TileHalfWidth
            && SnowStormMath.Abs(human.MoveTargetY - y) < SnowStormMath.TileHalfWidth;
    }

    private static bool HasAvailable(SnowStormSnowballSource source, Dictionary<int, int> reserved) =>
        source.SnowballCount > reserved.GetValueOrDefault(source.Id);

    private SnowStormScheduledEvent Schedule(int turn, int subturn, SnowStormEvent gameEvent)
    {
        arena.Schedule(turn, subturn, gameEvent);

        return new SnowStormScheduledEvent(turn, subturn, gameEvent);
    }

    // The AIR client only sends throws it may make; a second throw before the first applies would spawn a ball
    // without ammo, so one pending throw per human at a time.
    private bool CanThrow(int humanId, int trajectory, [NotNullWhen(true)] out SnowStormHuman? human)
    {
        human = arena.GetObject(humanId) as SnowStormHuman;

        return human is { CanThrowSnowballs: true }
            && trajectory is >= SnowStormSnowball.TrajectoryQuickThrow and <= SnowStormSnowball.TrajectoryDefaultThrow
            && !arena.PendingEvents().OfType<SnowStormCreateSnowball>().Any(pending => pending.HumanId == humanId);
    }

    private (int X, int Y) SpreadSpawn(List<(int X, int Y)> chosen, Random random)
    {
        for (var minDistance = 12; minDistance >= 0; minDistance -= 2) {
            int minDistanceSquared = Math.Max(1, minDistance * minDistance);
            var candidates = new List<(int X, int Y)>();

            for (var y = 0; y < arena.Level.Height; y++) {
                for (var x = 0; x < arena.Level.Width; x++) {
                    if (arena.IsWalkable(x, y)
                        && chosen.All(used => (used.X - x) * (used.X - x) + (used.Y - y) * (used.Y - y) >= minDistanceSquared)) {
                        candidates.Add((x, y));
                    }
                }
            }

            if (candidates.Count > 0) {
                return candidates[random.Next(candidates.Count)];
            }
        }

        throw new InvalidOperationException("The arena has no free walkable tile for another spawn.");
    }

    private int ClampX(int x) => Math.Clamp(x, 0, SnowStormMath.TileToWorld(arena.Level.Width - 1));

    private int ClampY(int y) => Math.Clamp(y, 0, SnowStormMath.TileToWorld(arena.Level.Height - 1));
}
