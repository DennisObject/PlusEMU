using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.SnowStorm.Simulation.SimulationTestSupport;

namespace Plus.Tests.SnowStorm.Simulation;

public class SnowStormSnowballTests
{
    // Polaris SnowWarAirParityTest vectors (tile (0,0) to (n,0)): [direction, ttl, parabolaOffset, planarVelocity, trajectory].
    [Theory]
    [InlineData(10, 0, 90, 10, 5, 2000, 0)]
    [InlineData(10, 1, 90, 17, 8, 1882, 1)]
    [InlineData(10, 2, 90, 22, 11, 1454, 2)]
    [InlineData(30, 1, 90, 33, 16, 1818, 1)]
    [InlineData(40, 2, 90, 70, 35, 1428, 2)]
    [InlineData(10, 3, 90, 10, 5, 2000, 0)]
    [InlineData(15, 3, 90, 26, 13, 1846, 1)]
    [InlineData(25, 3, 90, 56, 28, 1421, 2)]
    public void InitialisesTrajectoriesLikeAirAndPolaris(int tiles, int trajectory, int direction, int ttl, int offset, int velocity, int resolved)
    {
        var snowball = new SnowStormSnowball(1);
        snowball.Initialize(0, 0, SnowStormSnowball.InitialHeight, trajectory, World(tiles), 0, null);

        Assert.Equal([direction, ttl, offset, velocity, resolved],
            new[] { snowball.Direction360, snowball.TimeToLive, snowball.ParabolaOffset, snowball.PlanarVelocity, snowball.Trajectory });
    }

    [Theory]
    [InlineData(42_500, 0)]
    [InlineData(48_500, 1)]
    public void DefaultThrowResolvesFromMidTileWorldCoordinates(int targetX, int resolved)
    {
        var snowball = new SnowStormSnowball(1);
        snowball.Initialize(500, 700, SnowStormSnowball.InitialHeight, SnowStormSnowball.TrajectoryDefaultThrow, targetX, 700, null);

        Assert.Equal(resolved, snowball.Trajectory);
    }

    [Fact]
    public void QuickThrowFliesFlatThenFallsAndLandsOnSubturnTwentyFour()
    {
        var arena = SnowStormArena.Create(OpenLevel(40, 10), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        int id = ThrowAtPosition(arena, thrower, World(30), World(5), SnowStormSnowball.TrajectoryQuickThrow);
        var heights = new List<int>();

        for (var turn = 0; turn < 7; turn++) {
            arena.RunTurn();
            heights.Add(((SnowStormSnowball)arena.GetObject(id)!).Z);
        }

        var snowball = (SnowStormSnowball)arena.GetObject(id)!;
        Assert.Equal([3000, 3000, 3000, 2760, 2250, 1560, 690], heights);
        Assert.Equal(World(2) + 21 * (256 * 2000 / 255), snowball.X);
        Assert.Equal(-11, snowball.TimeToLive);
        arena.RunTurn();
        Assert.Null(arena.GetObject(id));
    }

    [Fact]
    public void LobPassesOverATreeAndQuickThrowHitsItWhenLowEnough()
    {
        var arena = SnowStormArena.Create(OpenLevel(40, 10, Tree(1, 5, 5), Tree(2, 14, 5)), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var nearTree = (SnowStormTree)arena.Objects.First(gameObject => gameObject is SnowStormTree);
        var farTree = (SnowStormTree)arena.Objects.Last(gameObject => gameObject is SnowStormTree);

        ThrowAtPosition(arena, thrower, World(10), World(5), SnowStormSnowball.TrajectoryLongLob);
        RunTurns(arena, 12);
        Assert.Equal(0, nearTree.Hits);
        Assert.Equal(0, farTree.Hits);

        ThrowAtPosition(arena, thrower, World(39), World(5), SnowStormSnowball.TrajectoryQuickThrow);
        RunTurns(arena, 10);
        Assert.Equal(0, nearTree.Hits);
        Assert.Equal(1, farTree.Hits);
    }

    [Fact]
    public void TreeStopsCollidingAfterMaxHits()
    {
        var arena = SnowStormArena.Create(OpenLevel(40, 10, Tree(1, 12, 5)), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var tree = (SnowStormTree)arena.Objects.First(gameObject => gameObject is SnowStormTree);
        var lastBall = 0;

        for (var throwIndex = 0; throwIndex < 4; throwIndex++) {
            lastBall = ThrowAtPosition(arena, thrower, World(39), World(5), SnowStormSnowball.TrajectoryQuickThrow);
            RunTurns(arena, 6);
        }

        Assert.Equal(3, tree.Hits);
        Assert.Equal(3, arena.Snapshot().First(snapshot => snapshot.Id == tree.Id).Variables[8]);
        Assert.True(((SnowStormSnowball)arena.GetObject(lastBall)!).X > World(12));
    }

    [Fact]
    public void PileAbsorbsLowBallsByItsSnowballCount()
    {
        var withPile = SnowStormArena.Create(OpenLevel(40, 10, Pile(1, 15, 5)), 2);
        var withoutPile = SnowStormArena.Create(OpenLevel(40, 10), 2);
        int pileBall = ThrowAtPosition(withPile, withPile.AddHuman(Player(1, 1), 2, 5, 2), World(39), World(5), 0);
        int openBall = ThrowAtPosition(withoutPile, withoutPile.AddHuman(Player(1, 1), 2, 5, 2), World(39), World(5), 0);

        RunTurns(withPile, 7);
        RunTurns(withoutPile, 7);

        Assert.Null(withPile.GetObject(pileBall));
        Assert.NotNull(withoutPile.GetObject(openBall));
        Assert.Equal(12, withPile.Piles.Single().SnowballCount);
    }

    [Fact]
    public void BallHitsGroundBelowTheSummedFuseHeight()
    {
        var arena = SnowStormArena.Create(OpenLevel(40, 10, Block(1, 6, 5, 3500)), 2);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        int id = ThrowAtPosition(arena, thrower, World(39), World(5), SnowStormSnowball.TrajectoryQuickThrow);

        arena.RunTurn();
        Assert.NotNull(arena.GetObject(id));
        arena.RunTurn();
        Assert.Null(arena.GetObject(id));
    }
}
