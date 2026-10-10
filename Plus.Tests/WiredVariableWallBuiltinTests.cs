using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableWallBuiltinTests
{
    [Fact]
    public void ActualWallProviderFeedsScalarSnapshotAndNativeWrites()
    {
        var f = new Fixture();
        var expected = new Dictionary<string, int>
        { ["@position.x"] = 3, ["@position.y"] = 7, ["@wallitem_offset"] = 12, ["@altitude"] = 341, ["@rotation"] = 0 };

        foreach (var (key, value) in expected) {
            Assert.Equal(value, f.Read(key)!.Value);
        }

        using (var reads = f.Module.CaptureReads(expected.Keys.Select(Reference), f.Frame)) {
            foreach (var (key, value) in expected) {
                Assert.Equal(value, reads.Read(Reference(key), f.Holder, f.Frame)!.Value);
            }
        }

        Assert.True(f.Write("@position.x", 4));
        Assert.True(f.Write("@position.y", 8));
        Assert.True(f.Write("@wallitem_offset", 13));
        Assert.True(f.Write("@altitude", 201));
        Assert.True(f.Write("@rotation", 1));
        Assert.Equal(":w=4,8 l=19,60 r a=201", f.Wall.WallCoordinates);
        Assert.Equal(4, f.Read("@position.x")!.Value);
        Assert.Equal(8, f.Read("@position.y")!.Value);
        Assert.Equal(19, f.Read("@wallitem_offset")!.Value);
        Assert.Equal(201, f.Read("@altitude")!.Value);
        Assert.Equal(1, f.Read("@rotation")!.Value);
        Assert.Empty(f.Module.DrainChanges());
        Assert.Equal(5, f.Store.Writes.Count);
        Assert.False(f.Write("@wallitem_offset", 33));
        Assert.False(f.Write("@rotation", 2));
        Assert.False(f.Module.Mutate(Reference("@position.x"), f.Holder, WiredVariableMutation.Set, 5, new(1, [f.Holder])));
        Assert.Equal(":w=4,8 l=19,60 r a=201", f.Wall.WallCoordinates);
        Assert.Empty(f.Module.DrainChanges());
        var moved = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_movedItems", Fixture.Private)!.GetValue(f.Room.GetRoomItemHandler())!;
        Assert.Empty(moved);
    }

    [Fact]
    public void MissingMalformedDetachedAndSameIdReplacementWallRemainAbsent()
    {
        var f = new Fixture();
        var keys = new[] { "@position.x", "@position.y", "@altitude", "@rotation", "@wallitem_offset" };
        var missing = new RoomWiredBuiltinVariables(f.Room);

        foreach (var key in keys) {
            Assert.Null(missing.Read(Reference(key), f.Holder, f.Frame));
        }

        f.Wall.WallCoordinates = "invalid";

        foreach (var key in keys) {
            Assert.Null(f.Read(key));
        }

        Assert.False(f.Write("@position.x", 3));
        f.Wall.WallCoordinates = ":w=3,7 l=12,16 l";
        f.Walls.TryRemove(f.Wall.Id, out _);

        foreach (var key in keys) {
            Assert.Null(f.Read(key));
        }

        f.Walls[f.Wall.Id] = new Item { Id = f.Wall.Id, OwnerId = 5, Definition = f.Wall.Definition, WallCoordinates = ":w=7,8 l=30,40 r" };

        foreach (var key in keys) {
            Assert.Null(f.Read(key));
        }

        using (var reads = f.Module.CaptureReads(keys.Select(Reference), f.Frame)) {
            foreach (var key in keys) {
                Assert.Null(reads.Read(Reference(key), f.Holder, f.Frame));
            }
        }

        Assert.False(f.Write("@position.x", 9));
        Assert.Equal(":w=7,8 l=30,40 r", f.Walls[f.Wall.Id].WallCoordinates);
        Assert.Empty(f.Module.DrainChanges());
    }

    [Fact]
    public void FloorCoordinatesKeepExistingNativeReadsWithoutWallDelegation()
    {
        var f = new Fixture();
        var floor = new Item { Id = 3, OwnerId = 5, Rotation = 2, Definition = new() { Type = ItemType.Floor } };
        floor.SetState(2, 3, 4.5, []);
        f.Floors[floor.Id] = floor;
        var holder = WiredVariableRuntimeFrames.FurniHolder(floor);
        var frame = new WiredVariableFrame(1, [holder]);
        var builtins = new RoomWiredBuiltinVariables(f.Room, engineRead: (_, _, _) => throw new InvalidOperationException("Unexpected wall delegation"));
        Assert.Equal(2, builtins.Read(Reference("@position.x"), holder, frame)!.Value);
        Assert.Equal(3, builtins.Read(Reference("@position.y"), holder, frame)!.Value);
        Assert.Equal(450, builtins.Read(Reference("@altitude"), holder, frame)!.Value);
        Assert.Equal(2, builtins.Read(Reference("@rotation"), holder, frame)!.Value);
    }

    private static WiredVariableReference Reference(string key) => new(WiredVariableTarget.Furni, "internal:" + key);
    private sealed class Fixture
    {
        public const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public Room Room { get; } = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        public Item Wall { get; } = new() { Id = 2, OwnerId = 5, RoomId = 1, Definition = new() { Type = ItemType.Wall }, ExtraData = new LegacyDataFormat { Data = "0" }, WallCoordinates = ":w=3,7 l=12,16 l" };
        public ConcurrentDictionary<uint, Item> Walls { get; }
        public ConcurrentDictionary<uint, Item> Floors { get; }
        public WiredVariableHolder Holder { get; }
        public WiredVariableFrame Frame { get; }
        public WiredVariableModule Module { get; }
        public WallStore Store { get; } = new();
        public Fixture()
        {
            Room.Id = 1;
            Room.OwnerId = 5;
            var handler = new RoomItemHandling(Room, Store, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
            typeof(Room).GetField("_roomItemHandling", Private)!.SetValue(Room, handler);
            var model = new RoomModel("wall", 1, 1, 0, 2,
                "xxxxxxxxxx\rx0xxxxxxxx\rx00000000x\rx00000000x\rx00000000x\rx00000000x\rx00000000x\rxxxxxxxxxx\rxxxxxxxxxx",
                0, 0, true, presentation: new(false, 0, 0, 0));
            var map = new Gamemap(Room, model, TestLogging.Navigation, TestRoomSettings.Empty,
                TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            typeof(Room).GetField("_gamemap", Private)!.SetValue(Room, map);
            typeof(Room).GetField("_roomUserManager", Private)!.SetValue(Room, new RoomUserManager(Room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
            var wired = new WiredComponent(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            typeof(Room).GetField("_wiredComponent", Private)!.SetValue(Room, wired);
            Walls = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_wallItems", Private)!.GetValue(handler)!;
            Floors = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", Private)!.GetValue(handler)!;
            Walls[Wall.Id] = Wall;
            typeof(Item).GetField("_room", Private)!.SetValue(Wall, Room);
            var context = new WiredRuntimeContext(Room, new(WiredEventKind.ClickFurni) { EventItem = Wall },
                new(() => Floors.Values, () => [], handler.GetItem), wired);
            context.Triggering.FurniIds.Add(Wall.Id);
            context.Policy.Addons.DisableAnimation = true;
            Frame = WiredVariableRuntimeFrames.Create(context);
            Assert.Contains(WiredVariableRuntimeFrames.FurniHolder(Wall), Frame.Holders);
            Holder = WiredVariableRuntimeFrames.FurniHolder(Wall);
            Module = new(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)), new RoomWiredBuiltinVariables(Room, wired.ReadBuiltin, wired.WriteBuiltin));
        }
        public WiredVariableValue? Read(string key) => Module.Read(Reference(key), Holder, Frame);
        public bool Write(string key, int value) => Module.Mutate(Reference(key), Holder, WiredVariableMutation.Set, value, Frame);
    }
    private sealed class WallStore : IRoomItemStore
    {
        public List<string> Writes { get; } = [];
        public void AssignOwner(uint itemId, int userId) { }
        public void ClearRoom(uint itemId) { }
        public void SaveWallPosition(uint itemId, string wallPosition) => Writes.Add(wallPosition);
        public void MoveWall(uint itemId, uint roomId, string wallPosition) => Writes.Add(wallPosition);
        public void SaveMoved(IReadOnlyList<RoomItemSave> items) { }
        public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) { }
        public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) { }
    }
    private sealed class Directory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
        public WiredVariableDefinition? Find(uint itemId) => null;
    }
}
