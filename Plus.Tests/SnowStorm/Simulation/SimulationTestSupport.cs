using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;

namespace Plus.Tests.SnowStorm.Simulation;

internal static class SimulationTestSupport
{
    public static SnowStormLevelData OpenLevel(int width, int height, params SnowStormFuseObject[] fuseObjects)
    {
        string row = new('0', width);

        return new SnowStormLevelData(width, height, string.Join('\r', Enumerable.Repeat(row, height)), fuseObjects);
    }

    public static SnowStormFuseObject Block(int id, int x, int y, int height = 2300) =>
        new("snst_block1", id, x, y, 1, 1, height, 0, 0, false, "0");

    public static SnowStormFuseObject Tree(int id, int x, int y, int height = 2300) =>
        new("snst_tree1", id, x, y, 1, 1, height, 0, 0, false, "0");

    public static SnowStormFuseObject Pile(int id, int x, int y) =>
        new("snst_ballpile", id, x, y, 1, 1, 0, 0, 0, false, "0");

    public static SnowStormFuseObject Machine(int id, int x, int y) =>
        new("s_snowball_machine", id, x, y, 1, 1, 2400, 0, 0, false, "0");

    public static SnowStormPlayer Player(int userId, int team) => new(userId, $"player{userId}", "hd-180-1", "M", team);

    public static int World(int tile) => SnowStormMath.TileToWorld(tile);

    public static void RunTurns(SnowStormArena arena, int turns)
    {
        for (var turn = 0; turn < turns; turn++) {
            arena.RunTurn();
        }
    }

    public static void RunUntil(SnowStormArena arena, Func<bool> condition, int maxTurns = 200)
    {
        for (var turn = 0; turn < maxTurns && !condition(); turn++) {
            arena.RunTurn();
        }

        Assert.True(condition(), "condition not reached");
    }

    // Throw + create pair at the next turn, like the server emits it.
    public static int ThrowAtPosition(SnowStormArena arena, SnowStormHuman thrower, int x, int y, int trajectory)
    {
        int snowballId = arena.AllocateObjectId();
        arena.Schedule(arena.Turn, 0, new SnowStormThrowAtPosition(thrower.Id, x, y, trajectory));
        arena.Schedule(arena.Turn, 0, new SnowStormCreateSnowball(snowballId, thrower.Id, x, y, trajectory));

        return snowballId;
    }

    public static int ThrowAtHuman(SnowStormArena arena, SnowStormHuman thrower, SnowStormHuman target, int trajectory)
    {
        int snowballId = arena.AllocateObjectId();
        arena.Schedule(arena.Turn, 0, new SnowStormThrowAtHuman(thrower.Id, target.Id, trajectory));
        arena.Schedule(arena.Turn, 0, new SnowStormCreateSnowball(snowballId, thrower.Id, target.X, target.Y, trajectory));

        return snowballId;
    }
}
