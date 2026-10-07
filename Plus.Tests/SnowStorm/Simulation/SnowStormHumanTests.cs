using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.SnowStorm.Simulation.SimulationTestSupport;

namespace Plus.Tests.SnowStorm.Simulation;

public class SnowStormHumanTests
{
    [Fact]
    public void StepTakesSixSubturnsAndOccupiesOnlyTheNextTile()
    {
        var arena = SnowStormArena.Create(OpenLevel(10, 10), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 2, 2);
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(human.Id, World(3), World(2)));

        arena.RunTurn();
        Assert.Equal(World(2) + 3 * SnowStormHuman.MoveSpeed, human.X);
        Assert.Equal((2, 3), (human.CurrentTileX, human.NextTileX));
        Assert.Same(human, arena.GetTile(3, 2)!.GameObject);
        Assert.Null(arena.GetTile(2, 2)!.GameObject);
        Assert.True(human.IsMoving);

        arena.RunTurn();
        Assert.Equal(World(3), human.X);
        Assert.Equal((3, 3), (human.CurrentTileX, human.NextTileX));

        arena.RunTurn();
        Assert.False(human.IsMoving);
    }

    [Fact]
    public void WalksAroundABlockWithTheThreeCandidateRule()
    {
        var arena = SnowStormArena.Create(OpenLevel(12, 12, Block(1, 4, 5)), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 5, 2);
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(human.Id, World(6), World(5)));
        var path = new List<(int, int)> { (2, 5) };

        for (var turn = 0; turn < 20; turn++) {
            arena.RunTurn();

            if (path[^1] != (human.CurrentTileX, human.CurrentTileY)) {
                path.Add((human.CurrentTileX, human.CurrentTileY));
            }
        }

        Assert.Equal([(2, 5), (3, 5), (4, 4), (5, 5), (6, 5)], path);
        Assert.Equal((World(6), World(5)), (human.X, human.Y));
    }

    [Fact]
    public void StopsBeforeABlockedTarget()
    {
        var arena = SnowStormArena.Create(OpenLevel(12, 12, Block(1, 4, 5)), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 5, 2);
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(human.Id, World(4), World(5)));

        RunTurns(arena, 10);

        Assert.Equal((3, 5), (human.CurrentTileX, human.CurrentTileY));
        Assert.Equal((World(3), World(5)), (human.MoveTargetX, human.MoveTargetY));
    }

    [Fact]
    public void WaitsWhenAllThreeCandidatesAreOccupied()
    {
        var arena = SnowStormArena.Create(OpenLevel(12, 12, Block(1, 4, 4), Block(2, 4, 6)), 2);
        var walker = arena.AddHuman(Player(1, 1), 3, 5, 2);
        var blocker = arena.AddHuman(Player(2, 2), 4, 5, 6);
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(walker.Id, World(8), World(5)));

        RunTurns(arena, 3);
        Assert.Equal((3, 5), (walker.CurrentTileX, walker.CurrentTileY));
        Assert.Equal(World(8), walker.MoveTargetX);

        arena.Schedule(arena.Turn, 0, new SnowStormNewMoveTarget(blocker.Id, World(4), World(9)));
        RunTurns(arena, 12);
        Assert.Equal((8, 5), (walker.CurrentTileX, walker.CurrentTileY));
    }

    [Fact]
    public void ThrowSnapsToTheNextTileFacesTheTargetAndStartsTheTimer()
    {
        var arena = SnowStormArena.Create(OpenLevel(12, 12), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 2, 4);
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(human.Id, World(6), World(2)));
        arena.RunTurn();

        ThrowAtPosition(arena, human, World(2), World(9), SnowStormSnowball.TrajectoryShortLob);
        arena.RunTurn();

        Assert.Equal((World(3), World(2)), (human.X, human.Y));
        Assert.Equal((World(3), World(2)), (human.MoveTargetX, human.MoveTargetY));
        Assert.Equal(4, human.BodyDirection);
        Assert.Equal(4, human.SnowballCount);
        Assert.Equal(2, human.ThrowTimer);
        Assert.False(human.CanThrowSnowballs);
        var snowball = arena.Objects.OfType<SnowStormSnowball>().Single();
        Assert.Equal(human.Id, snowball.GetVariable(8));
    }

    [Fact]
    public void HitsKnockDownAfterFiveAndScoreSix()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var victim = arena.AddHuman(Player(2, 2), 5, 5, 0);

        for (var hit = 1; hit <= 4; hit++) {
            ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
            RunTurns(arena, 2);
            Assert.Equal(5 - hit, victim.HitPoints);
            Assert.Equal(hit, thrower.Score);
        }

        ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
        RunTurns(arena, 2);

        Assert.True(victim.IsStunned);
        Assert.Equal(0, victim.HitPoints);
        Assert.Equal(6, victim.BodyDirection);
        Assert.Equal(10, thrower.Score);
        Assert.Equal([10, 0], arena.TeamScores);
        var throwerStats = arena.GetStats(thrower.Id);
        var victimStats = arena.GetStats(victim.Id);
        Assert.Equal((1, 5, 5), (throwerStats.Kills, throwerStats.SnowballHits, throwerStats.SnowballsThrown));
        Assert.Equal((1, 5), (victimStats.Deaths, victimStats.SnowballHitsTaken));
    }

    [Fact]
    public void StunLastsOneHundredSubturnsThenFiftyNineInvincible()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var victim = arena.AddHuman(Player(2, 2), 5, 5, 6);

        for (var hit = 0; hit < 5; hit++) {
            ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
            RunTurns(arena, 2);
        }

        Assert.True(victim.IsStunned);
        int lastSubturn = arena.Turn * SnowStormArena.SubturnsPerTurn - 1;
        int knockdown = lastSubturn - (SnowStormHuman.StunTime - victim.ActivityTimer);

        while (arena.Turn < 80) {
            arena.RunTurn();
            int subturn = arena.Turn * SnowStormArena.SubturnsPerTurn - 1;
            var (state, timer) = subturn < knockdown + 100 ? (2, 100 - (subturn - knockdown))
                : subturn < knockdown + 159 ? (3, 59 - (subturn - knockdown - 100))
                : (0, 0);
            Assert.Equal((state, timer), (victim.ActivityState, victim.ActivityTimer));
            Assert.Equal(state == 2 ? 0 : 5, victim.HitPoints);
        }
    }

    [Fact]
    public void StunnedAndInvincibleHumansLetBallsThrough()
    {
        var arena = SnowStormArena.Create(OpenLevel(30, 10), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var victim = arena.AddHuman(Player(2, 2), 5, 5, 6);

        for (var hit = 0; hit < 5; hit++) {
            ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
            RunTurns(arena, 2);
        }

        arena.Schedule(arena.Turn, 0, new SnowStormStartMakingSnowball(thrower.Id));
        RunTurns(arena, 7);
        Assert.Equal(1, thrower.SnowballCount);
        int stunnedBall = ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
        RunTurns(arena, 3);
        Assert.True(((SnowStormSnowball)arena.GetObject(stunnedBall)!).X > victim.X);

        RunUntil(arena, () => victim.IsInvincible);
        arena.Schedule(arena.Turn, 0, new SnowStormStartMakingSnowball(thrower.Id));
        RunTurns(arena, 7);
        int invincibleBall = ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
        RunTurns(arena, 3);
        Assert.True(victim.IsInvincible);
        Assert.True(((SnowStormSnowball)arena.GetObject(invincibleBall)!).X > victim.X);
        Assert.Equal(5, victim.HitPoints);
        Assert.Equal(2, arena.GetStats(thrower.Id).SnowballsCreated);
    }

    [Fact]
    public void MakingASnowballEndsInvincibility()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var victim = arena.AddHuman(Player(2, 2), 5, 5, 6);

        for (var hit = 0; hit < 5; hit++) {
            ThrowAtHuman(arena, thrower, victim, SnowStormSnowball.TrajectoryQuickThrow);
            RunTurns(arena, 2);
        }

        RunUntil(arena, () => victim.IsInvincible);
        ThrowAtPosition(arena, victim, World(10), World(5), SnowStormSnowball.TrajectoryQuickThrow);
        arena.RunTurn();
        arena.Schedule(arena.Turn, 0, new SnowStormStartMakingSnowball(victim.Id));
        arena.RunTurn();

        Assert.Equal(SnowStormHuman.StateMakingSnowball, victim.ActivityState);
        RunTurns(arena, 7);
        Assert.Equal(SnowStormHuman.StateNormal, victim.ActivityState);
        Assert.Equal(5, victim.SnowballCount);
    }

    [Fact]
    public void MoveTargetCancelsMakingWithoutAddingABall()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 5, 2);
        ThrowAtPosition(arena, human, World(10), World(5), SnowStormSnowball.TrajectoryQuickThrow);
        arena.RunTurn();
        arena.Schedule(arena.Turn, 0, new SnowStormStartMakingSnowball(human.Id));
        arena.Schedule(arena.Turn + 2, 1, new SnowStormNewMoveTarget(human.Id, World(4), World(5)));

        RunTurns(arena, 10);

        Assert.Equal(4, human.SnowballCount);
        Assert.Equal(SnowStormHuman.StateNormal, human.ActivityState);
        Assert.Equal((4, 5), (human.CurrentTileX, human.CurrentTileY));
    }

    [Fact]
    public void TeammateAbsorbsTheBallWithoutDamage()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var teammate = arena.AddHuman(Player(2, 1), 4, 5, 6);
        var opponent = arena.AddHuman(Player(3, 2), 7, 5, 6);
        int id = ThrowAtHuman(arena, thrower, opponent, SnowStormSnowball.TrajectoryQuickThrow);

        RunTurns(arena, 6);

        Assert.Null(arena.GetObject(id));
        Assert.Equal(5, teammate.HitPoints);
        Assert.Equal(5, opponent.HitPoints);
        Assert.Equal(0, thrower.Score);
        Assert.Equal(1, arena.GetStats(thrower.Id).FriendlyHits);
        Assert.Equal([0, 0], arena.TeamScores);
    }

    [Fact]
    public void HumanLeftGameIsRemovedAndFreesItsTile()
    {
        var arena = SnowStormArena.Create(OpenLevel(10, 10), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 2, 2);
        arena.Schedule(0, 1, new SnowStormHumanLeftGame(human.Id));

        arena.RunTurn();

        Assert.Null(arena.GetObject(human.Id));
        Assert.True(arena.IsWalkable(2, 2));
        Assert.Empty(arena.Snapshot());
    }
}
