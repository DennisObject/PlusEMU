using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.SnowStorm.Simulation.SimulationTestSupport;

namespace Plus.Tests.SnowStorm.Simulation;

public class SnowStormServerRulesTests
{
    [Fact]
    public void MachineRefillsOnceEveryHundredAndOneSubturnsUpToFive()
    {
        var arena = SnowStormArena.Create(OpenLevel(10, 10, Machine(1, 4, 4)), 2);
        var rules = new SnowStormServerRules(arena);
        var refills = new List<SnowStormScheduledEvent>();

        for (var turn = 0; turn < 250; turn++) {
            arena.RunTurn();
            refills.AddRange(rules.ScheduleRefillsAndPickups());
        }

        Assert.All(refills, scheduled => Assert.IsType<SnowStormMachineCreatesSnowball>(scheduled.Event));
        Assert.Equal([(34, 1), (68, 0), (101, 2), (135, 1), (169, 0)], refills.Select(scheduled => (scheduled.Turn, scheduled.Subturn)));
        Assert.Equal(5, arena.Machines.Single().SnowballCount);
    }

    [Fact]
    public void HumanStandingOnThePickupTileTakesOneBallEveryTwentySubturns()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10, Machine(1, 4, 4)), 2);
        var rules = new SnowStormServerRules(arena);
        var human = arena.AddHuman(Player(1, 1), 4, 5, 0);

        for (var throwIndex = 0; throwIndex < 3; throwIndex++) {
            Assert.True(rules.TryScheduleThrowAtPosition(arena.Turn, 0, human.Id, World(15), World(5), 0));
            RunTurns(arena, 2);
        }

        var machine = arena.Machines.Single();

        for (var refill = 0; refill < 5; refill++) {
            arena.Schedule(arena.Turn, 0, new SnowStormMachineCreatesSnowball(machine.Id));
        }

        var pickups = new List<SnowStormScheduledEvent>();

        for (var turn = 0; turn < 40; turn++) {
            arena.RunTurn();
            pickups.AddRange(rules.ScheduleRefillsAndPickups().Where(scheduled => scheduled.Event is SnowStormHumanGetsSnowball));
        }

        Assert.Equal(3, pickups.Count);
        Assert.Equal(20, (pickups[1].Turn * 3 + pickups[1].Subturn) - (pickups[0].Turn * 3 + pickups[0].Subturn));
        Assert.Equal(5, human.SnowballCount);
        Assert.Equal(3, arena.GetStats(human.Id).SnowballsFromMachine);
        // Five prefilled, three taken, one generator refill on the 101st evaluated subturn.
        Assert.Equal(5 - 3 + 1, machine.SnowballCount);
    }

    [Fact]
    public void PilePickupNeedsManhattanDistanceOneAndAStandingHuman()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10, Pile(1, 4, 4)), 2);
        var rules = new SnowStormServerRules(arena);
        var adjacent = arena.AddHuman(Player(1, 1), 5, 4, 0);
        var diagonal = arena.AddHuman(Player(2, 1), 5, 5, 0);
        Assert.True(rules.TryScheduleThrowAtPosition(0, 0, adjacent.Id, World(15), World(4), 0));
        Assert.True(rules.TryScheduleThrowAtPosition(0, 0, diagonal.Id, World(15), World(9), 0));

        for (var turn = 0; turn < 10; turn++) {
            arena.RunTurn();
            rules.ScheduleRefillsAndPickups();
        }

        Assert.Equal(5, adjacent.SnowballCount);
        Assert.Equal(4, diagonal.SnowballCount);
        Assert.Equal(11, arena.Piles.Single().SnowballCount);
    }

    [Fact]
    public void ThrowIsTwoEventsInOneSubturnWithAFreshBallId()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var rules = new SnowStormServerRules(arena);
        var thrower = arena.AddHuman(Player(1, 1), 2, 5, 2);
        var teammate = arena.AddHuman(Player(2, 1), 2, 7, 2);
        var opponent = arena.AddHuman(Player(3, 2), 8, 5, 6);

        Assert.False(rules.TryScheduleThrowAtHuman(0, 1, thrower.Id, teammate.Id, 3));
        Assert.False(rules.TryScheduleThrowAtHuman(0, 1, thrower.Id, opponent.Id, 4));
        Assert.True(rules.TryScheduleThrowAtHuman(0, 1, thrower.Id, opponent.Id, 3));
        Assert.False(rules.TryScheduleThrowAtPosition(1, 0, thrower.Id, 0, 0, 0));

        Assert.Equal(
            [
                new SnowStormScheduledEvent(0, 1, new SnowStormThrowAtHuman(thrower.Id, opponent.Id, 3)),
                new SnowStormScheduledEvent(0, 1, new SnowStormCreateSnowball(4, thrower.Id, World(8), World(5), 3))
            ],
            arena.GetScheduledEvents(0));

        arena.RunTurn();
        Assert.False(rules.TryScheduleThrowAtPosition(arena.Turn, 0, thrower.Id, 0, 0, 0));
        arena.RunTurn();
        Assert.True(rules.TryScheduleThrowAtPosition(arena.Turn, 0, thrower.Id, -500, 999_999, 0));
        Assert.Equal(new SnowStormThrowAtPosition(thrower.Id, 0, World(9), 0), arena.GetScheduledEvents(arena.Turn)[0].Event);
    }

    [Fact]
    public void MakeSnowballIsRejectedWhenFullOrAlreadyPending()
    {
        var arena = SnowStormArena.Create(OpenLevel(20, 10), 2);
        var rules = new SnowStormServerRules(arena);
        var human = arena.AddHuman(Player(1, 1), 2, 5, 2);

        Assert.False(rules.TryScheduleMakeSnowball(0, 0, human.Id));
        Assert.True(rules.TryScheduleThrowAtPosition(0, 0, human.Id, World(10), World(5), 0));
        arena.RunTurn();
        Assert.True(rules.TryScheduleMakeSnowball(arena.Turn, 0, human.Id));
        Assert.False(rules.TryScheduleMakeSnowball(arena.Turn, 2, human.Id));
    }

    [Fact]
    public void SpawnsUseFreeTeamTilesThenSpreadOutAndFaceTheCentre()
    {
        var arena = SnowStormArena.Create(ArcticIslandFixture.Level(), 2);
        var rules = new SnowStormServerRules(arena);
        var teamTiles = new Dictionary<int, IReadOnlyList<(int X, int Y)>>
        {
            [1] = ArcticIslandFixture.NorthSpawns,
            [2] = ArcticIslandFixture.SouthSpawns
        };

        var spawns = rules.ChooseSpawns([1, 2, 1, 2, 1, 1, 1, 1], teamTiles, new Random(7));

        Assert.Equal(8, spawns.Distinct().Count());
        Assert.All(spawns.Take(7), spawn => Assert.Contains((spawn.X, spawn.Y), ArcticIslandFixture.NorthSpawns.Concat(ArcticIslandFixture.SouthSpawns)));
        Assert.DoesNotContain((spawns[7].X, spawns[7].Y), ArcticIslandFixture.NorthSpawns);
        Assert.All(spawns, spawn => Assert.True(arena.IsWalkable(spawn.X, spawn.Y)));
        Assert.All(spawns, spawn => Assert.Equal(SnowStormServerRules.DirectionTowardsCenter(spawn.X, spawn.Y), spawn.BodyDirection));
        Assert.Equal(4, SnowStormServerRules.DirectionTowardsCenter(25, 10));
        Assert.Equal(0, SnowStormServerRules.DirectionTowardsCenter(25, 40));

        var open = SnowStormArena.Create(OpenLevel(30, 30), 2);
        var spread = new SnowStormServerRules(open).ChooseSpawns([1, 2], null, new Random(1));
        var (first, second) = (spread[0], spread[1]);
        Assert.True((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y) >= 144);
    }
}
