using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games;
using Plus.HabboHotel.Rooms.Games.Banzai;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class RoomGameTimeTests
{
    private static readonly TimeZoneInfo NonUtcZone =
        TimeZoneInfo.CreateCustomTimeZone("game-minus-seven", TimeSpan.FromHours(-7), "test", "test");

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void RegenerateMapsUsesStrictUtcMinuteBoundaryWithOneClockRead(int deltaMilliseconds, bool expected)
    {
        var now = new DateTimeOffset(2040, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var clock = new CountingClock(now, NonUtcZone);
        var room = World(clock);
        var previous = now - TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(deltaMilliseconds);
        room.LastRegenerationAt = previous;
        clock.ResetReads();

        var result = new RegenerateMapsBox(room, Furni()).Execute(Array.Empty<object>());

        Assert.Equal(expected, result);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(expected ? now : previous, room.LastRegenerationAt);
    }

    [Fact]
    public void GameManagerStopCapturesUtcResetInstantOnce()
    {
        var now = new DateTimeOffset(2041, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var clock = new CountingClock(now, NonUtcZone);
        var room = World(clock);
        clock.ResetReads();

        room.GetGameManager().StopGame();

        Assert.Equal(now, room.LastTimerResetAt);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public void BanzaiEndUsesStrictFiveSecondBoundaryAndSharesItsInstantWithGameManager()
    {
        var startedAt = new DateTimeOffset(2042, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var clock = new CountingClock(startedAt, NonUtcZone);
        var room = World(clock);
        var banzai = room.GetBanzai();
        typeof(BattleBanzai).GetField("_startedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(banzai, startedAt);
        typeof(BattleBanzai).GetField("<IsBanzaiActive>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(banzai, true);
        clock.ResetReads();

        Assert.False(banzai.HasMinimumPlayTimeAt(startedAt.AddTicks(-1)));
        Assert.False(banzai.HasMinimumPlayTimeAt(startedAt.AddSeconds(5)));
        Assert.True(banzai.HasMinimumPlayTimeAt(startedAt.AddSeconds(5).AddTicks(1)));

        var endedAt = startedAt.AddSeconds(6);
        clock.SetUtcNow(endedAt);
        clock.ResetReads();
        banzai.BanzaiEnd(triggeredByUser: true);

        Assert.False(banzai.IsBanzaiActive);
        Assert.Equal(endedAt, room.LastTimerResetAt);
        Assert.Equal(1, clock.Reads);
    }

    private static Room World(TimeProvider clock)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("game-time", 0, 0, 0, 0, "000\r000\r000", 0, 0, true), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set(room, "_interactionClock", clock);
        Set(room, "_gamemap", map);
        Set(room, "_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        Set(room, "_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, clock, new TestRewardProgress()));
        TestRoomUserSnapshots.Install(room);
        Set(room, "_wiredComponent", new WiredComponent(room, TestLogging.Logger, clock, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused));
        Set(room, "_gameManager", new GameManager(room, clock));
        Set(room, "_banzai", new BattleBanzai(room, clock));
        room.GetGameMap().GenerateMaps();
        return room;
    }

    private static Item Furni() => new()
    {
        Id = 1,
        Definition = new ItemDefinition
        {
            Type = ItemType.Floor,
            Width = 1,
            Length = 1,
            Modes = 1,
            ItemName = "wf_act_regenerate_maps",
            PublicName = "",
            AdjustableHeights = [],
            VendingIds = []
        }
    };

    private static void Set(Room room, string field, object value) =>
        typeof(Room).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);

    private sealed class CountingClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public int Reads { get; private set; }
        public override TimeZoneInfo LocalTimeZone => zone;
        public override DateTimeOffset GetUtcNow() { Reads++; return _now; }
        public void SetUtcNow(DateTimeOffset value) => _now = value;
        public void ResetReads() => Reads = 0;
    }
}
