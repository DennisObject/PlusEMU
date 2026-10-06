using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

[Collection("Pathfinding room adapter")]
public class LegacyContractsTests
{
    [Fact]
    public void RetainedStressAndStaffContractsRunOnProductionV2Search()
    {
        var rows = new[] { "xxxxx", "x000x", "x0x0x", "x000x", "xxxxx" };
        var (grid, _, _) = NavTest.Create(5, 5, states: string.Concat(rows).Select(c => c == 'x' ? SquareState.Blocked : SquareState.Open).ToArray());
        var occupancy = new PlanningOccupancy(25);
        Array.Fill(occupancy.Targets, TargetOccupancy.Stationary);
        var stress = new ActorProfile { IgnoreUsers = true };
        var staff = new ActorProfile { LegacyOverride = true };
        var ws = new PathWorkspace(25, 25); var route = new Route(); var search = new PathSearch(grid, new());
        Assert.Equal(PathOutcome.Found, search.Find(new(stress, grid.Position(11), 3, 2, occupancy), ws, route));
        Assert.DoesNotContain(route.Steps.ToArray(), p => p.Tile == 12);
        Assert.Equal(PathOutcome.Found, search.Find(new(staff, grid.Position(11), 3, 2, occupancy), ws, route));
        Assert.Contains(route.Steps.ToArray(), p => p.Tile == 12);
        var (cliff, _, _) = NavTest.Create(3, 1, z: [0, 2, 0]);
        Assert.Equal(PathOutcome.Unreachable, new PathSearch(cliff, new()).Find(new(stress, cliff.Position(0), 2, 0), ws, route));
        Assert.Equal(PathOutcome.Found, new PathSearch(cliff, new()).Find(new(staff, cliff.Position(0), 2, 0), ws, route));
    }
    [Theory]
    // Steps are -1 when unreachable. The original legacy A* cuts corners that v2's official corner rule blocks.
    [InlineData("00000|00000|00000|00000|00000", 1, 1, 4, 4, 3, 3)]
    [InlineData("00000|0xxx0|000x0|0xxx0|00000", 1, 2, 4, 2, 6, 9)]
    [InlineData("00000|00100|01210|00100|00000", 1, 2, 3, 2, 2, 2)]
    [InlineData("00000|00x00|0x000|00000|00000", 1, 1, 2, 2, 1, 6)]
    [InlineData("00x00|00x00|00x00|00x00|00x00", 1, 1, 4, 4, -1, -1)]
    public void LegacyCorpusCharacterizesOriginalVersusV2(string terrain, int sx, int sy, int gx, int gy, int legacySteps, int v2Steps)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var model = new RoomModel("fixed-legacy-parity", 0, 0, 0, 0, terrain.Replace('|', '\r'), 0, 0, false);
        var map = new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set("_gamemap", map); Set("_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards)); Set("_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        map.GenerateMaps(); NavTest.Enable(map).Compiler.RebuildAll();
        var grid = map.Navigation.Grid;
        var legacyActor = new RoomUser(0, 0, 1, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = sx, Y = sy };
        var path = PathFinder.FindPath(legacyActor, true, map, new(sx, sy), new(gx, gy));
        var route = new Route(); var search = new PathSearch(grid, new());
        var outcome = search.Find(new(new ActorProfile(), grid.Position(grid.Tile(sx, sy)), gx, gy),
            new PathWorkspace(grid.SlotCapacity, grid.ActiveNodeCount), route);
        Assert.Equal(legacySteps, path.Count - 1); // Legacy includes the origin.
        Assert.Equal(v2Steps, outcome == PathOutcome.Found ? route.Count : -1);
        foreach (var (from, to) in path.Zip(path.Skip(1)))
            Assert.True(map.IsValidStep(to, from, from.X == gx && from.Y == gy, false, false, legacyActor));

        void Set(string name, object value) => typeof(Room).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);
    }

}
