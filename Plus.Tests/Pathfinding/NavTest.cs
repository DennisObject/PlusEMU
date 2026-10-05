using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.Tests.Pathfinding;

internal static class NavTest
{
    public static (NavGrid Grid, NavInputs Inputs, NavGridCompiler Compiler) Create(int width = 4, int height = 4,
        PathfindingSettings? settings = null, double[]? z = null, SquareState[]? states = null, int door = -1)
    {
        var grid = new NavGrid(width, height, z ?? new double[width * height], states ?? new SquareState[width * height], door);
        var inputs = new NavInputs(width, height);
        var compiler = new NavGridCompiler(grid, inputs, settings ?? new());
        compiler.RebuildAll();
        return (grid, inputs, compiler);
    }
    public static NavItemRecord Record(uint id, long version, int[] tiles, double z = 0, double h = 0,
        bool walkable = true, bool seat = false, InteractionType interaction = InteractionType.None,
        string state = "0", int group = 7, bool removed = false) => new(id, version, z, h, walkable, seat, interaction, state, group, 0, removed, Array.AsReadOnly(tiles));
    public static RoomNavigation Enable(Gamemap map)
    {
        var room = (Room)typeof(Gamemap).GetField("_room", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(map)!;
        var navigation = new RoomNavigation(room, map.StaticModel, new() { Engine = PathfindingEngine.Shadow }, TestLogging.Navigation, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(map, navigation);
        foreach (var item in room.GetRoomItemHandler().GetFloor) navigation.Inputs.Attach(item);
        return navigation;
    }
    public static Item Item(uint id = 1, int length = 1, int width = 1) => new()
    {
        Id = id, Definition = new ItemDefinition { Type = ItemType.Floor, Length = length, Width = width,
            Height = 0, Walkable = true, AdjustableHeights = new(), InteractionType = InteractionType.None }
    };
}
