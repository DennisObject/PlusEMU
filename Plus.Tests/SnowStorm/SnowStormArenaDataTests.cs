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
    public void OfficialBackdropsAreDecorationThatLeavesTheSimulationUnchanged()
    {
        var arenas = SnowStormTestSupport.Arenas();

        foreach (var (fieldType, y, offsetX, offsetY, offsetZ) in new[] { (8, 19, "-1160", "1554", "10000"), (9, 22, "-1106", "1496", "9920"), (11, 22, "-1096", "1444", "9950") }) {
            Assert.True(arenas.TryGet(fieldType, out var arena));
            var url = SnowStormSettings.Default.Backgrounds[fieldType];
            var game = SnowStormArenas.ForGame(arena, url);
            var backdrop = game.Level.FuseObjects[^1];
            Assert.Equal(new SnowStormFuseObject("ads_background", arena.Level.FuseObjects.Count + 1, 0, y, 1, 1, 0, 1, 0, true, "0"), backdrop);
            Assert.Equal(url, game.MapStuff[backdrop.Id].Single(pair => pair.Key == "imageUrl").Value);
            Assert.Equal(new[] { offsetX, offsetY, offsetZ },
                new[] { "offsetX", "offsetY", "offsetZ" }.Select(key => game.MapStuff[backdrop.Id].Single(pair => pair.Key == key).Value));

            var with = SnowStormArena.Create(game.Level, 2);
            var without = SnowStormArena.Create(arena.Level, 2);
            Assert.Equal(without.CalculateChecksum(0), with.CalculateChecksum(0));

            for (var tileY = 0; tileY < arena.Level.Height; tileY++) {
                for (var tileX = 0; tileX < arena.Level.Width; tileX++) {
                    Assert.Equal(without.IsWalkable(tileX, tileY), with.IsWalkable(tileX, tileY));
                }
            }

            Assert.Same(arena.Level, SnowStormArenas.ForGame(arena, "").Level);
        }

        var settings = SnowStormSettings.Read(new SnowStormTestSupport.Settings(("gamecenter.snowwar.artic.bg", ""), ("gamecenter.snowwar.dragoncave.bg", " https://cdn/bg.png ")));
        Assert.Equal(("", "https://cdn/bg.png", "/c_images/snowstorm_client/official/snst_bg_3_noscale.png"),
            (settings.Backgrounds[8], settings.Backgrounds[9], settings.Backgrounds[11]));
    }

    [Fact]
    public void InvalidArenaFilesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00", "0"], "items": [] }"""));
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00"], "items": ["unknown_furni 0 0 0"] }"""));
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00"], "items": ["snst_block1 5 0 0"] }"""));
        Assert.Throws<InvalidDataException>(() => SnowStormArenas.Parse("""{ "fieldType": 1, "heightmap": ["00"], "items": [], "backdrop": { "x": 0, "y": 3 } }"""));
        var parsed = SnowStormArenas.Parse("""{ "fieldType": 1, "name": "Tiny", "heightmap": ["0x"], "items": ["snst_fence 0 0 2"], "spawns": { "1": [[0, 0]] } }""");
        Assert.Equal(new SnowStormFuseObject("snst_fence", 1, 0, 0, 1, 2, 960, 2, 0, false, "0"), Assert.Single(parsed.Level.FuseObjects));
        Assert.Equal("0x", parsed.Level.HeightMap);
        Assert.Equal([(0, 0)], parsed.Spawns[1]);
    }
}
