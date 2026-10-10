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
        var wired = new WiredComponent(room, TestLogging.Logger, clock, TestRoomSettings.Empty, new Factory(zone),
            TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);

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

    [Theory]
    [InlineData("", 16, 2040)]
    [InlineData("Europe/Berlin", 0, 2041)]
    public void CalendarBuiltinAndTimeUtilityShareTheEffectiveRoomTimezone(string zone, int hour, int year)
    {
        var clock = new CountingClock();
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var wired = new WiredComponent(room, TestLogging.Logger, clock, TestRoomSettings.Empty, new Factory(zone),
            TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        var frame = new Plus.HabboHotel.Items.Wired.Variables.WiredVariableFrame(room.Id, []);
        var target = Plus.HabboHotel.Items.Wired.Variables.WiredVariableTarget.Global;
        Assert.Equal(hour, wired.ReadBuiltin(new(target, "@current_time.hour_of_day"), new(target, 0, 0), frame));
        Assert.Equal(year, wired.ReadBuiltin(new(target, "@current_time.year"), new(target, 0, 0), frame));
        Assert.Equal(wired.CalendarTime.Offset, TimeZoneInfo.ConvertTime(clock.Now, wired.Variables.TimeZone()).Offset);
        Assert.Equal(zone, wired.Settings.Snapshot.TimeZoneId);
    }

    [Theory]
    [InlineData("UTC", 22, 1)]
    [InlineData("Europe/Berlin", 0, 2)]
    public void NativeCalendarConditionsEvaluateTheNamedTimezone(string zone, int hour, int day)
    {
        var instant = new DateTimeOffset(2040, 7, 1, 22, 0, 0, TimeSpan.Zero);
        Assert.True(WiredBoxRegistry.TryGet("wf_cnd_match_time", out var time));
        var timeNative = WiredNativeEditorProjection.DefaultNative(time) with
        {
            OwnedIntParams = [0, 0, 1, 0, 59, 0, 59, hour, hour],
            Text = zone
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(1, time, timeNative, out var timeConfig));
        Assert.True(WiredTimeConditions.MatchesTime(timeConfig, instant));
        Assert.True(WiredBoxRegistry.TryGet("wf_cnd_match_date", out var date));
        var dateNative = WiredNativeEditorProjection.DefaultNative(date) with
        {
            OwnedIntParams = [1, 0, 127, day, day, 4095, 2040, 2040],
            Text = zone
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(1, date, dateNative, out var dateConfig));
        Assert.True(WiredTimeConditions.MatchesDate(dateConfig, instant));
        Assert.False(WiredNativeEditorProjection.TryCompile(1, time, timeNative with { Text = "Not/AZone" }, out _));
    }

    private sealed class CountingClock : TimeProvider
    {
        public DateTimeOffset Now { get; } = new(2040, 12, 31, 23, 30, 0, TimeSpan.Zero);
        public int Reads { get; private set; }
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("TestLocal", TimeSpan.FromHours(-7), "TestLocal", "TestLocal");
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }

    private sealed class Factory(string zone) : IWiredRoomSettingsFactory, IWiredRoomSettingsStore
    {
        public WiredRoomSettings Create(Room room) => new(room, this);
        public WiredRoomSettingsSnapshot? Load(uint roomId) => new(TimeZoneId: zone);
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings) => throw new NotSupportedException();
    }
}
