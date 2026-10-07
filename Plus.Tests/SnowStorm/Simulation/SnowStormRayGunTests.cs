using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.SnowStorm.Simulation.SimulationTestSupport;

namespace Plus.Tests.SnowStorm.Simulation;

public class SnowStormRayGunTests
{
    private static SnowStormFuseObject Gun(int id, int x, int y, int direction) =>
        new(SnowStormRayGun.FuseName, id, x, y, 1, 2, 1600, direction, 0, false, "0");

    [Fact]
    public void ArcticIslandUseTilesSitBehindTheEffectiveFootprint()
    {
        var arena = SnowStormArena.Create(ArcticIslandFixture.Level(), 2);

        Assert.Equal([(28, 12, 4, 28, 11), (41, 33, 6, 43, 33), (31, 41, 0, 31, 43), (17, 37, 2, 16, 37), (11, 21, 2, 10, 21)],
            arena.RayGuns.Select(gun => (gun.X, gun.Y, gun.Direction, gun.UseX, gun.UseY)));
        Assert.All(arena.RayGuns, gun => Assert.True(arena.IsWalkable(gun.UseX, gun.UseY)));
        Assert.False(arena.IsWalkable(42, 33));
    }

    [Fact]
    public void OnlyCardinalRayGunsCount()
    {
        var arena = SnowStormArena.Create(OpenLevel(10, 10, Gun(1, 4, 4, 3), Block(2, 6, 6)), 2);

        Assert.Empty(arena.RayGuns);
    }

    [Theory]
    [InlineData(2, 9, 10, 25, 10)]
    [InlineData(6, 12, 10, -5, 10)]
    [InlineData(4, 10, 9, 10, 25)]
    [InlineData(0, 10, 12, 10, -5)]
    public void BurstSpawnsSevenDefaultThrowsAtTheTargetsAheadOfTheGun(int direction, int useX, int useY, int centerX, int centerY)
    {
        var arena = SnowStormArena.Create(OpenLevel(40, 40, Gun(7, 10, 10, direction)), 2);
        var gun = Assert.Single(arena.RayGuns);
        Assert.Equal((useX, useY), (gun.UseX, gun.UseY));
        var human = arena.AddHuman(Player(1, 1), useX, useY, (direction + 4) % 8);
        int first = arena.AllocateObjectIds(SnowStormRayGun.BurstSize);
        arena.Schedule(0, 0, new SnowStormRayGunBurst(human.Id, gun.FuseObjectId, first));

        var result = arena.RunTurn();

        (int X, int Y)[] targets =
        [
            (centerX, centerY), (centerX, centerY + 1), (centerX + 1, centerY), (centerX - 1, centerY + 1),
            (centerX - 1, centerY - 1), (centerX + 1, centerY - 1), (centerX + 1, centerY + 1)
        ];
        Assert.Equal(targets, gun.BurstTargets());
        Assert.Equal(100, Assert.Single(result.AppliedEvents).Event.Type);
        Assert.Equal(direction, human.BodyDirection);
        Assert.Equal(5, human.SnowballCount);
        Assert.Equal(2, human.ThrowTimer);
        var snowballs = arena.Objects.OfType<SnowStormSnowball>().ToList();
        Assert.Equal(Enumerable.Range(first, 7), snowballs.Select(snowball => snowball.Id));

        for (var index = 0; index < targets.Length; index++) {
            var expected = new SnowStormSnowball(99);
            expected.Initialize(World(useX), World(useY), SnowStormSnowball.InitialHeight, SnowStormSnowball.TrajectoryDefaultThrow,
                World(targets[index].X), World(targets[index].Y), human);
            Assert.Equal((expected.Direction360, expected.Trajectory, expected.TimeToLive, expected.PlanarVelocity),
                (snowballs[index].Direction360, snowballs[index].Trajectory, snowballs[index].TimeToLive + 3, snowballs[index].PlanarVelocity));
            Assert.Same(human, snowballs[index].Thrower);
        }

        Assert.Equal(first + 7, arena.AllocateObjectId());
    }

    [Fact]
    public void BurstHitsScoreForTheShooterWithoutSpendingAmmo()
    {
        var arena = SnowStormArena.Create(OpenLevel(30, 12, Gun(1, 5, 5, 2)), 2);
        var shooter = arena.AddHuman(Player(1, 1), 4, 5, 2);
        var opponent = arena.AddHuman(Player(2, 2), 19, 5, 6);
        var rules = new SnowStormServerRules(arena);

        arena.RunTurn();
        var burst = Assert.Single(rules.ScheduleRayGunBursts());
        RunTurns(arena, 15);

        Assert.Equal(new SnowStormRayGunBurst(shooter.Id, 1, 3), burst.Event);
        var stats = arena.GetStats(shooter.Id);
        Assert.True(stats.SnowballHits > 0);
        Assert.Equal(5 - stats.SnowballHits, opponent.HitPoints);
        Assert.Equal(stats.SnowballHits, shooter.Score);
        Assert.Equal([shooter.Score, 0], arena.TeamScores);
        Assert.Equal(5, shooter.SnowballCount);
        Assert.Equal(0, stats.SnowballsThrown);
    }

    [Fact]
    public void FiresOncePerArrivalAndRespectsTheCooldown()
    {
        var arena = SnowStormArena.Create(OpenLevel(30, 12, Gun(1, 5, 5, 2)), 2);
        var rules = new SnowStormServerRules(arena);
        var first = arena.AddHuman(Player(1, 1), 4, 2, 4);
        var second = arena.AddHuman(Player(2, 2), 1, 5, 2);
        var bursts = new List<SnowStormScheduledEvent>();

        void Run(int turns, Func<bool>? until = null)
        {
            for (var turn = 0; turn < turns && until?.Invoke() != true; turn++) {
                arena.RunTurn();
                bursts.AddRange(rules.ScheduleRayGunBursts());
            }
        }

        // Walking across the use tile does not fire; stopping on it fires once.
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(first.Id, World(4), World(8)));
        Run(15);
        Assert.Equal((4, 8), (first.CurrentTileX, first.CurrentTileY));
        Assert.Empty(bursts);
        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(first.Id, World(4), World(5)));
        Run(15, () => bursts.Count > 0);
        var burst = Assert.Single(bursts);
        Assert.Equal((first.Id, arena.Turn, 0), (((SnowStormRayGunBurst)burst.Event).HumanId, burst.Turn, burst.Subturn));

        // An arrival inside the 60-subturn cooldown is used up without firing, also after the cooldown runs out.
        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(first.Id, World(4), World(8)));
        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(second.Id, World(4), World(5)));
        Run(8);
        Assert.Equal((4, 5), (second.CurrentTileX, second.CurrentTileY));
        Assert.True((arena.Turn - burst.Turn) * SnowStormArena.SubturnsPerTurn < SnowStormServerRules.RayGunCooldown);
        Run(30);
        Assert.Single(bursts);

        // Leaving and coming back fires again.
        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(second.Id, World(1), World(5)));
        Run(10);
        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(second.Id, World(4), World(5)));
        Run(10);
        Assert.Equal(2, bursts.Count);
        Assert.Equal(second.Id, ((SnowStormRayGunBurst)bursts[1].Event).HumanId);
        Assert.Equal(((SnowStormRayGunBurst)bursts[0].Event).FirstSnowballId + SnowStormRayGun.BurstSize, ((SnowStormRayGunBurst)bursts[1].Event).FirstSnowballId);
    }

    [Fact]
    public void MakingASnowballOnTheUseTileDelaysTheBurstUntilItEnds()
    {
        var arena = SnowStormArena.Create(OpenLevel(30, 12, Gun(1, 5, 5, 2)), 2);
        var rules = new SnowStormServerRules(arena);
        var human = arena.AddHuman(Player(1, 1), 4, 6, 2);
        ThrowAtPosition(arena, human, World(20), World(6), SnowStormSnowball.TrajectoryQuickThrow);
        arena.RunTurn();
        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(human.Id, World(4), World(5)));
        arena.Schedule(arena.Turn, 2, new SnowStormStartMakingSnowball(human.Id));
        var bursts = new List<SnowStormScheduledEvent>();

        for (var turn = 0; turn < 20; turn++) {
            arena.RunTurn();

            if (human.ActivityState == SnowStormHuman.StateMakingSnowball) {
                Assert.Empty(rules.ScheduleRayGunBursts());
            }
            else {
                bursts.AddRange(rules.ScheduleRayGunBursts());
            }
        }

        Assert.Equal((4, 5), (human.CurrentTileX, human.CurrentTileY));
        Assert.Equal(5, human.SnowballCount);
        var burst = Assert.Single(bursts);
        Assert.True(burst.Turn > 7);
    }

    [Fact]
    public void EveryNorthSpawnWalksStraightOntoTheNorthGun()
    {
        foreach (var (x, y) in ArcticIslandFixture.NorthSpawns) {
            var arena = SnowStormArena.Create(ArcticIslandFixture.Level(), 2);
            var rules = new SnowStormServerRules(arena);
            var human = arena.AddHuman(Player(1, 1), x, y, 4);
            arena.Schedule(0, 0, new SnowStormNewMoveTarget(human.Id, World(28), World(11)));
            var bursts = new List<SnowStormScheduledEvent>();

            for (var turn = 0; turn < 40; turn++) {
                arena.RunTurn();
                bursts.AddRange(rules.ScheduleRayGunBursts());
            }

            Assert.Equal(1, bursts.Count);
        }
    }
}
