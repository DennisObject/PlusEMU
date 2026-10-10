using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredConditionParityTests
{
    [Fact]
    public void CounterTimeKeepsMinuteZeroAndRejectsValuesOutsideTheEditor()
    {
        Assert.True(Validates(0, 0));
        Assert.False(Validates(100, 0));
        Assert.False(Validates(99, 120));
        Assert.Equal((true, false), (Executes(0, 0, 0), Executes(0, 0, 500)));
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(99, 119)]
    public void CounterTimeMatchesMinutesSixtyAndNinetyNineAndRejectsOneHalfSecondOff(int minutes, int halves)
    {
        var exact = minutes * 60_000L + halves * 500L;

        Assert.Equal((true, true, false), (Validates(minutes, halves), Executes(minutes, halves, exact), Executes(minutes, halves, exact + 500)));
    }

    [Fact]
    public void NegativeFurniOnAndAvatarOnQuantifiersMatchTheEmptyLabels()
    {
        var mixed = Scene(stackOnFirst: true, stackOnSecond: false, avatarOnFirst: true, avatarOnSecond: false);
        var empty = Scene(stackOnFirst: false, stackOnSecond: false, avatarOnFirst: false, avatarOnSecond: false);
        var occupied = Scene(stackOnFirst: true, stackOnSecond: true, avatarOnFirst: true, avatarOnSecond: true);

        Expect("wf_cnd_has_furni_on", "wf_cnd_furnis_hv_avtrs", mixed, true, false, true, false, "positive mixed");
        Expect("wf_cnd_has_furni_on", "wf_cnd_furnis_hv_avtrs", empty, false, false, false, false, "positive all empty");
        Expect("wf_cnd_has_furni_on", "wf_cnd_furnis_hv_avtrs", occupied, true, true, true, true, "positive all occupied");
        Expect("wf_cnd_not_furni_on", "wf_cnd_not_hv_avtrs", empty, true, true, true, true, "negative all empty");
        Expect("wf_cnd_not_furni_on", "wf_cnd_not_hv_avtrs", occupied, false, false, false, false, "negative all occupied");
        Expect("wf_cnd_not_furni_on", "wf_cnd_not_hv_avtrs", mixed, true, false, true, false, "negative mixed");
    }

    private static void Expect(string furniName, string avatarName, Occupancy scene,
        bool furniAny, bool furniAll, bool avatarAny, bool avatarAll, string label)
    {
        var actual = (Run(furniName, 0, scene), Run(furniName, 1, scene), Run(avatarName, 0, scene), Run(avatarName, 1, scene));
        var expected = (furniAny, furniAll, avatarAny, avatarAll);

        Assert.True(actual == expected, $"{label}: expected {expected} actual {actual}");
    }

    private static bool Validates(int minutes, int halves)
    {
        var (room, _, items) = World();
        items[7] = Floor(7, "wf_upcounter1", 0, 0);
        var box = Box(room, "wf_cnd_counter_time_matches", _ => 0);

        return WiredNativeTestSupport.TryValidateRuntime(box, Counter(minutes, halves, 7), out _, out _);
    }

    private static bool Executes(int minutes, int halves, long actual)
    {
        var (room, _, items) = World();
        var counter = Floor(7, "wf_upcounter1", 0, 0);
        items[counter.Id] = counter;
        var box = Box(room, "wf_cnd_counter_time_matches", item => item.Id == counter.Id ? actual : null);
        var proposed = Counter(minutes, halves, counter.Id);

        if (WiredNativeTestSupport.TryValidateRuntime(box, proposed, out var config, out _)) {
            box.ApplyConfiguration(config);
        }
        else {
            box.ApplyConfiguration(proposed);
        }

        var matched = box.Execute(Context(room, [counter], []));
        Assert.Equal(new[] { 1, minutes, halves, 100, 0 }, box.Configuration.IntParams);

        return matched;
    }

    private static bool Run(string name, int radio, Occupancy scene)
    {
        var box = Box(scene.Room, name, _ => null);
        var native = WiredNativeEditorProjection.DefaultNative(box.Descriptor) with
        {
            OwnedIntParams = [radio],
            FurniSourceTypes = [100],
            PrimaryItems = [new(scene.First.Id, false), new(scene.Second.Id, false)]
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, native, out var proposed));
        Assert.True(box.TryValidateConfiguration(proposed, out var config, out var error), error);
        Assert.Equal(new[] { radio, 100 }, config.IntParams);
        box.ApplyConfiguration(config);
        var matched = box.Execute(Context(scene.Room, scene.Present, scene.Users));
        Assert.Equal(new[] { radio, 100 }, box.Configuration.IntParams);

        return matched;
    }

    private static Occupancy Scene(bool stackOnFirst, bool stackOnSecond, bool avatarOnFirst, bool avatarOnSecond)
    {
        var (room, map, items) = World();
        var first = Floor(1, "subject", 0, 0);
        var second = Floor(2, "subject", 1, 0);
        Place(map, items, first);
        Place(map, items, second);

        if (stackOnFirst) {
            Place(map, items, Floor(11, "stack", 0, 0));
        }

        if (stackOnSecond) {
            Place(map, items, Floor(12, "stack", 1, 0));
        }

        var users = new List<RoomUser>();

        if (avatarOnFirst) {
            users.Add(Avatar(room, 1, 0, 0));
        }

        if (avatarOnSecond) {
            users.Add(Avatar(room, 2, 1, 0));
        }

        if (users.Count == 0) {
            users.Add(Avatar(room, 3, 2, 2));
        }

        return new(room, first, second, items.Values.ToArray(), users.ToArray());
    }

    private static WiredConfiguration Counter(int minutes, int halves, uint counterId) => new()
    {
        IntParams = [1, minutes, halves, 100, 0],
        SelectedItems = [counterId]
    };

    private static WiredModernCondition Box(Room room, string name, Func<Item, long?> counterTime)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));

        // Native pick validation reads the box item's live room.
        var item = Floor(50, name, 2, 2);
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);

        return new(room, item, descriptor, TestGroupManager.Empty, counterTime, () => DateTimeOffset.UnixEpoch);
    }

    private static void Place(Gamemap map, ConcurrentDictionary<uint, Item> items, Item item)
    {
        items[item.Id] = item;
        map.AddToMap(item);
    }

    private static RoomUser Avatar(Room room, int id, int x, int y)
    {
        var user = new RoomUser(id, 0, id, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        user.SetPos(x, y, 0);

        return user;
    }

    private static Item Floor(uint id, string name, int x, int y)
    {
        var item = new Item
        {
            Id = id,
            ExtraData = new LegacyDataFormat { Data = "0" },
            Definition = new()
            {
                Type = ItemType.Floor,
                ItemName = name,
                InteractionName = name,
                Width = 1,
                Length = 1,
                Modes = 2,
                AdjustableHeights = [],
                VendingIds = [],
                PublicName = name
            }
        };
        item.SetState(x, y, 0, Gamemap.GetAffectedTiles(1, 1, x, y, 0));

        return item;
    }

    private static (Room Room, Gamemap Map, ConcurrentDictionary<uint, Item> Items) World()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        typeof(Room).GetField("_interactionClock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, TimeProvider.System);
        var map = new Gamemap(room, new RoomModel("wired-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, true),
            TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room,
            new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(),
                TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        TestRoomUserSnapshots.Install(room);
        typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[map.Model.MapSizeX, map.Model.MapSizeY]);
        typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[map.Model.MapSizeX, map.Model.MapSizeY]);

        return (room, map, (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!);
    }

    private static WiredRuntimeContext Context(Room room, Item[] items, RoomUser[] users) =>
        new(room, new(WiredEventKind.Use), new(() => items, () => users), new UnusedOperations());

    private sealed record Occupancy(Room Room, Item First, Item Second, Item[] Present, RoomUser[] Users);

    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
