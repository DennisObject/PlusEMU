using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;

namespace Plus.Tests.SnowStorm;

public class SnowStormArenaDataTests
{
    [Fact]
    public void OfficialArenasLoadAsAirLevelData()
    {
        var arenas = SnowStormTestSupport.Arenas();
        Assert.Equal([8, 9, 11], arenas.All.Select(arena => arena.FieldType).Order());
        Assert.Equal(["Arctic Island", "Dragon Top", "Fight Night"], arenas.All.OrderBy(arena => arena.FieldType).Select(arena => arena.Name));

        foreach (var arena in arenas.All) {
            var level = arena.Level;
            Assert.Equal((50, 50), (level.Width, level.Height));
            Assert.Equal(level.Height, level.HeightMap.Split('\r').Length);
            Assert.Equal(Enumerable.Range(1, level.FuseObjects.Count), level.FuseObjects.Select(item => item.Id));
            Assert.All(level.FuseObjects, item => Assert.True(item.X < level.Width && item.Y < level.Height && !item.CanStandOn && item.Stuff == "0"));
            var simulation = SnowStormArena.Create(level, SnowStormSettings.TeamCount);
            Assert.All(arena.Spawns.Values.SelectMany(tiles => tiles), tile => Assert.True(simulation.IsWalkable(tile.X, tile.Y), $"{arena.Name} spawn {tile}"));
        }

        // Arctic Island is the AIR-era layout: one central snowball machine, stacked blocks and two team spawn zones.
        Assert.True(arenas.TryGet(8, out var arctic));
        Assert.Equal(102, arctic.Level.FuseObjects.Count);
        var machine = Assert.Single(arctic.Level.FuseObjects, item => item.Name == "s_snowball_machine");
        Assert.Equal((26, 24, 1, 1, 1600), (machine.X, machine.Y, machine.XDimension, machine.YDimension, machine.Height));
        Assert.Equal(18, arctic.Level.FuseObjects.Count(item => item.Name == "snst_tree1"));
        Assert.All(arctic.Level.FuseObjects.Where(item => item.Altitude > 0), item => Assert.Equal(("snst_block1", 1440), (item.Name, item.Altitude)));
        Assert.Equal([5, 5], arctic.Spawns.OrderBy(team => team.Key).Select(team => team.Value.Count));
        var simulationArctic = SnowStormArena.Create(arctic.Level, 2);
        Assert.Single(simulationArctic.Machines);

        Assert.True(arenas.TryGet(9, out var dragon));
        Assert.Equal(20, SnowStormArena.Create(dragon.Level, 2).Piles.Count());
        Assert.True(arenas.TryGet(11, out var fight));
        Assert.Equal(12, SnowStormArena.Create(fight.Level, 2).Piles.Count());
        Assert.Equal([0, 1280, 2480], fight.Level.FuseObjects.Where(item => item.Name.StartsWith("xm09_man_")).OrderBy(item => item.Name).Select(item => item.Altitude));
        Assert.Empty(fight.Spawns);
    }

    [Fact]
    public void InvalidArenaFilesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00", "0"], "items": [] }"""));
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00"], "items": ["unknown_furni 0 0 0"] }"""));
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00"], "items": ["snst_block1 5 0 0"] }"""));
        var parsed = SnowStormArenas.Parse("""{ "fieldType": 1, "name": "Tiny", "heightmap": ["0x"], "items": ["snst_fence 0 0 2"], "spawns": { "1": [[0, 0]] } }""");
        Assert.Equal(new SnowStormFuseObject("snst_fence", 1, 0, 0, 1, 2, 960, 2, 0, false, "0"), Assert.Single(parsed.Level.FuseObjects));
        Assert.Equal("0x", parsed.Level.HeightMap);
        Assert.Equal([(0, 0)], parsed.Spawns[1]);
    }
}
