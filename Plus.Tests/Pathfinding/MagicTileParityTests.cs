using System.Reflection;
using Plus.Core.Settings;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Tests.Performance;
using Xunit;

namespace Plus.Tests.Pathfinding;

[Collection("Pathfinding room adapter")]
public class MagicTileParityTests
{
    [Theory]
    [InlineData(InteractionType.None, false, true, true)]
    [InlineData(InteractionType.None, true, true, true)]
    [InlineData(InteractionType.Bed, false, true, true)]
    [InlineData(InteractionType.Gate, false, true, true)]
    [InlineData(InteractionType.GuildGate, false, true, true)]
    [InlineData(InteractionType.None, false, false, true)]
    [InlineData(InteractionType.None, false, true, false)]
    [InlineData(InteractionType.None, false, false, false)]
    [InlineData(InteractionType.None, true, true, false)]
    [InlineData(InteractionType.None, true, false, false)]
    public void WalkMagicAndRaisedStacktoolAgreeWithLegacyHeightStateAndSearch(
        InteractionType underlyingInteraction, bool voidTile, bool collision, bool walkMagic)
    {
        WithSettings(collision, settings =>
        {
            var fixture = RoomPerformanceFixture.Create(0, 0);
            var model = new RoomModel("magic-parity", 0, 0, 0, 0,
                voidTile ? "0000\r0x00\r0000\r0000" : "0000\r0000\r0000\r0000", 0, 0, false);
            var map = new Gamemap(fixture.Room, model, TestLogging.Navigation, settings, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Room, map);
            var handler = fixture.Room.GetRoomItemHandler();
            var underlying = NavTest.Item(1);
            underlying.Definition.Height = walkMagic ? 6 : 1;
            underlying.Definition.Walkable = !walkMagic;
            underlying.Definition.InteractionType = underlyingInteraction;
            underlying.Definition.IsSeat = walkMagic && underlyingInteraction == InteractionType.None;
            Add(underlying, 0);

            if (walkMagic) {
                var low = Helper(100, InteractionType.WalkMagicTile);
                low.Definition.Height = 100;
                Add(low, 0.75);
                Add(Helper(10, InteractionType.WalkMagicTile), 1.25);
                Add(Helper(11, InteractionType.WalkMagicTile), 1.25);
            }
            else {
                Add(Helper(2, InteractionType.Stacktool), 2);
            }

            map.GenerateMaps();
            var navigation = map.Navigation!;
            navigation.Compiler.RebuildAll();
            var grid = navigation.Grid;
            var tile = grid.Tile(1, 1);
            Assert.Equal(map.SqAbsoluteHeight(1, 1), grid.LegacyZ[tile]);
            Assert.Equal(map.GameMap[1, 1] != 0, grid.Active(tile));

            if (walkMagic) {
                Assert.Equal(1.25, grid.WalkZ[tile]);
                Assert.Equal(SurfaceKind.WalkMagic, grid.Kind[tile]);
                Assert.Equal((uint)11, grid.SupportItem[tile]);
                Assert.Equal(NavFlags.Transit, grid.Flags[tile]);
                Assert.False(grid.TileVoid[tile]);
                Assert.Same(handler.GetItem(11), map.WalkMagicAt(1, 1));
            }

            var actor = new RoomUser(0, 0, 1, fixture.Room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 0, Y = 1 };
            var legacy = PathFinder.FindPath(actor, true, map, new(0, 1), new(1, 1));
            var route = new Route();
            var outcome = new PathSearch(grid, navigation.Settings).Find(new(new(), grid.Position(4), 1, 1),
                new PathWorkspace(grid.SlotCapacity, grid.ActiveNodeCount), route);
            Assert.Equal(legacy.Count > 0, outcome == PathOutcome.Found);

            if (legacy.Count > 0) {
                Assert.Equal(legacy.Count - 1, route.Count);
            }

            void Add(Item item, double z)
            {
                item.SetState(1, 1, z, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
                Assert.True(handler.AdmitFloorItem(item));
            }
        });
    }

    [Fact]
    public void WalkMagicOnVoidFlankFollowsOfficialCornerRuleAfterRemoval()
    {
        WithSettings(true, settings =>
        {
            var fixture = RoomPerformanceFixture.Create(0, 0);
            var map = new Gamemap(fixture.Room, new RoomModel("magic-corner", 0, 0, 0, 0,
                "0000\r00x0\r0000\r0000", 0, 0, false), TestLogging.Navigation, settings, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Room, map);
            var item = Helper(1, InteractionType.WalkMagicTile);
            item.SetState(2, 1, 0.75, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0));
            Assert.True(fixture.Room.GetRoomItemHandler().AdmitFloorItem(item));
            var navigation = map.Navigation!;
            Check(true);
            navigation.Inputs.Remove(item);
            var floor = (System.Collections.Concurrent.ConcurrentDictionary<uint, Item>)
                typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(fixture.Room.GetRoomItemHandler())!;
            Assert.True(floor.TryRemove(item.Id, out _));
            Check(false);

            void Check(bool expected)
            {
                map.GenerateMaps();
                navigation.Compiler.ApplyNow();
                var grid = navigation.Grid;
                Assert.Equal(expected, new MovementRules(grid, navigation.Settings).CanStep(new(), grid.Position(5),
                    grid.Position(10), StepPurpose.Goal, OccupancyView.Planning).Ok);
            }
        });
    }

    private static Item Helper(uint id, InteractionType interaction)
    {
        var item = NavTest.Item(id);
        item.Definition.InteractionType = interaction;
        item.Definition.Walkable = false;

        return item;
    }

    private static void WithSettings(bool collision, Action<ISettingsManager> test)
        => test(new Settings(collision));

    private sealed class Settings(bool collision) : ISettingsManager
    {
        public string TryGetValue(string key) => TryGetValue(key, "0");
        public string TryGetValue(string key, string defaultValue) => GetOptionalValue(key) ?? defaultValue;
        public string? GetOptionalValue(string key) => key switch
        {
            "pathfinding.engine" => "shadow",
            "pathfinding.stacktool_legacy_collision" => collision ? "1" : "0",
            _ => null
        };
        public Task Reload() => Task.CompletedTask;
    }
}
