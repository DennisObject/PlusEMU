using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.SnowStorm.Simulation.SimulationTestSupport;

namespace Plus.Tests.SnowStorm.Simulation;

public class SnowStormArenaTests
{
    [Fact]
    public void ChecksumIsSeedPlusWeightedVariablesInInsertionOrder()
    {
        var arena = SnowStormArena.Create(OpenLevel(5, 5, Machine(7, 1, 1)), 2);
        var human = arena.AddHuman(Player(42, 1), 2, 3, 2);

        Assert.Equal([4, 1, 3200, 3200, 0, 5, 0, 7], arena.Snapshot()[0].Variables);
        Assert.Equal([5, 2, 6400, 9600, 2, 3, 2, 5, 5, 0, 0, 0, 2, 3, 6400, 9600, 0, 1, 42], arena.Snapshot()[1].Variables);
        Assert.Equal(["player42", "", "hd-180-1", "M"], arena.Snapshot()[1].Strings!);

        // 253983 = iterateSeed(0); 22492 = machine; 308220 = human.
        Assert.Equal(584695, arena.RunTurn().Checksum);
        Assert.Equal(SnowStormMath.IterateSeed(1) + 22492 + 308220, arena.RunTurn().Checksum);
        Assert.Equal(2, human.Id);
    }

    [Fact]
    public void ChecksumWrapsAtInt32()
    {
        var arena = SnowStormArena.Create(OpenLevel(5, 5), 1);
        arena.ApplyFullStatus([new SnowStormObjectSnapshot([1, 9, int.MaxValue, 0, 0, 0, 0, 0, 0, 0, 0])], 0, 0);

        Assert.Equal(unchecked(SnowStormMath.IterateSeed(0) + 1 + 18 + int.MaxValue * 3), arena.CalculateChecksum(0));
    }

    [Fact]
    public void EventsApplyAtTheirSubturnBeforeObjectsMove()
    {
        var arena = SnowStormArena.Create(OpenLevel(10, 10), 2);
        var human = arena.AddHuman(Player(1, 1), 2, 2, 2);
        arena.Schedule(0, 2, new SnowStormNewMoveTarget(human.Id, World(5), World(2)));

        var result = arena.RunTurn();

        Assert.Equal(World(2) + SnowStormHuman.MoveSpeed, human.X);
        Assert.Equal(new SnowStormScheduledEvent(0, 2, new SnowStormNewMoveTarget(human.Id, World(5), World(2))), Assert.Single(result.AppliedEvents));
        Assert.Equal(1, arena.Turn);
    }

    [Fact]
    public void SchedulingIntoThePastIsRejected()
    {
        var arena = SnowStormArena.Create(OpenLevel(5, 5), 2);
        arena.RunTurn();

        Assert.Throws<ArgumentOutOfRangeException>(() => arena.Schedule(0, 0, new SnowStormMachineCreatesSnowball(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.Schedule(1, 3, new SnowStormMachineCreatesSnowball(1)));
        arena.Schedule(3, 1, new SnowStormMachineCreatesSnowball(1));
        arena.Schedule(3, 0, new SnowStormMachineCreatesSnowball(2));
        Assert.Equal([0, 1], arena.GetScheduledEvents(3).Select(scheduled => scheduled.Subturn));
    }

    [Fact]
    public void EventsForMissingObjectsAreDropped()
    {
        var arena = SnowStormArena.Create(OpenLevel(5, 5), 2);
        arena.Schedule(0, 0, new SnowStormNewMoveTarget(99, 0, 0));
        arena.Schedule(0, 0, new SnowStormCreateSnowball(5, 99, 0, 0, 0));

        Assert.Empty(arena.RunTurn().AppliedEvents);
        Assert.Empty(arena.Snapshot());
    }

    [Fact]
    public void ArcticIslandFootprintsFollowTheAirRules()
    {
        var arena = SnowStormArena.Create(ArcticIslandFixture.Level(), 2);

        Assert.Equal(50, arena.Level.Width);
        Assert.Null(arena.GetTile(0, 0));
        Assert.Equal(2880, arena.GetTile(2, 20)!.Height);
        Assert.False(arena.IsWalkable(2, 20));
        Assert.Equal(1440, arena.GetTile(41, 37)!.Height);
        Assert.Equal(0, arena.GetTile(19, 41)!.Height);
        Assert.False(arena.IsWalkable(19, 41));
        Assert.False(arena.IsWalkable(17, 14));
        Assert.False(arena.IsWalkable(18, 14));
        Assert.True(arena.IsWalkable(17, 15));
        Assert.Equal(1600, arena.GetTile(26, 24)!.Height);
        Assert.True(arena.IsWalkable(26, 25));

        var machine = Assert.Single(arena.Machines);
        Assert.Equal((26, 24, 0, 5), (machine.TileX, machine.TileY, machine.SnowballCount, machine.MaxSnowballs));
        Assert.Equal(18, arena.Objects.OfType<SnowStormTree>().Count());
        Assert.All(arena.Objects.OfType<SnowStormTree>(), tree => Assert.Equal((3200, 3, 0), (tree.Height, tree.MaxHits, tree.Hits)));
    }

    [Fact]
    public void FullStatusRebuildContinuesWithTheSameChecksums()
    {
        var level = ArcticIslandFixture.Level();
        var source = SnowStormArena.Create(level, 2);
        var north = source.AddHuman(Player(1, 1), 25, 12, 4);
        var south = source.AddHuman(Player(2, 2), 30, 43, 0);
        source.Schedule(0, 0, new SnowStormNewMoveTarget(north.Id, World(25), World(20)));
        source.Schedule(0, 1, new SnowStormNewMoveTarget(south.Id, World(30), World(36)));
        RunTurns(source, 6);
        ThrowAtPosition(source, north, World(26), World(30), SnowStormSnowball.TrajectoryLongLob);
        var last = source.RunTurn();

        var restored = SnowStormArena.Create(level, 2);
        restored.ApplyFullStatus(source.Snapshot(), last.Turn, last.Checksum);
        Assert.Equal(last.Checksum, restored.RunTurn().Checksum);

        for (var turn = 0; turn < 40; turn++) {
            if (turn == 10) {
                source.Schedule(source.Turn, 1, new SnowStormNewMoveTarget(south.Id, World(31), World(30)));
                restored.Schedule(restored.Turn, 1, new SnowStormNewMoveTarget(south.Id, World(31), World(30)));
            }

            Assert.Equal(source.RunTurn(), restored.RunTurn(), TurnResultComparer.Instance);
        }

        Assert.Equal(source.Snapshot().Select(snapshot => snapshot.Variables), restored.Snapshot().Select(snapshot => snapshot.Variables));
    }

    private sealed class TurnResultComparer : IEqualityComparer<SnowStormTurnResult>
    {
        public static readonly TurnResultComparer Instance = new();

        public bool Equals(SnowStormTurnResult? x, SnowStormTurnResult? y) =>
            x!.Turn == y!.Turn && x.Checksum == y.Checksum && x.AppliedEvents.SequenceEqual(y.AppliedEvents);

        public int GetHashCode(SnowStormTurnResult obj) => obj.Turn;
    }
}
