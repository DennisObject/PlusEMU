using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public class WiredCalendarClockTests
{
    [Theory]
    [InlineData("", 2040, -7)]
    [InlineData("Europe/Berlin", 2041, 1)]
    public void CalendarAndDateDefaultsUseOneRoomClockRead(string zone, int year, int offsetHours)
    {
        var clock = new CountingClock();
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var wired = new WiredComponent(room, TestLogging.Logger, clock, new Factory(zone),
            TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);

        var calendar = wired.CalendarTime;
        Assert.Equal(year, calendar.Year);
        Assert.Equal(TimeSpan.FromHours(offsetHours), calendar.Offset);
        Assert.Equal(clock.Now, calendar);
        Assert.Equal(1, clock.Reads);

        Assert.True(WiredBoxRegistry.TryGet("wf_cnd_match_date", out var descriptor));
        var item = new Item
        {
            Id = 1,
            ExtraData = new LegacyDataFormat { Data = "0" },
            Definition = new() { ItemName = "wf_cnd_match_date", InteractionName = "wf_cnd_match_date" }
        };
        var box = Assert.IsType<WiredModernCondition>(wired.CreateConfiguredBox(item, descriptor));
        var configuration = box.Configuration;
        Assert.Equal(year, configuration.IntParams[6]);
        Assert.Equal(year, configuration.IntParams[7]);
        Assert.Equal(2, clock.Reads);
    }

    private sealed class CountingClock : TimeProvider
    {
        public DateTimeOffset Now { get; } = new(2040, 12, 31, 23, 30, 0, TimeSpan.Zero);
        public int Reads { get; private set; }
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("TestLocal", TimeSpan.FromHours(-7), "TestLocal", "TestLocal");
        public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
    }

    private sealed class Factory(string zone) : IWiredRoomSettingsFactory, IWiredRoomSettingsStore
    {
        public WiredRoomSettings Create(Room room) => new(room, this);
        public WiredRoomSettingsSnapshot? Load(uint roomId) => new(TimeZoneId: zone);
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings) => throw new NotSupportedException();
    }
}
