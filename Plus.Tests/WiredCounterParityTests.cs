using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Xunit;

namespace Plus.Tests;

public sealed class WiredCounterParityTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(99)]
    public void ClockTriggerAcceptsTheEditorsFullMinuteRange(int minutes)
    {
        Assert.True(WiredTriggerConfiguration.TryValidate("wf_trg_clock_counter",
            new() { IntParams = [minutes, 119, 100] }, out var config, out _));
        var target = minutes * 60000L + 59500;
        Assert.False(WiredTriggerPredicates.MatchesCounter(config, target - 1000, target - 500));
        Assert.True(WiredTriggerPredicates.MatchesCounter(config, target - 500, target));
    }

    [Fact]
    public void PauseKeepsTheValueAndResumeDoesNotStopOrRescheduleARunningClock()
    {
        var item = new Item { Id = 1, Definition = new() { ItemName = "wf_upcounter1" }, ExtraData = new LegacyDataFormat { Data = "10" } };
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        Assert.True(clocks.Control(item, 0, 0));
        Assert.True(clocks.Control(item, 3, 100));
        Assert.False(clocks.IsRunning(item));
        Assert.Equal(10000, clocks.ReadMilliseconds(item));
        clocks.Poll(1000);
        Assert.Equal(10000, clocks.ReadMilliseconds(item));

        Assert.True(clocks.Control(item, 4, 1000));
        Assert.True(clocks.Control(item, 4, 1250));
        Assert.True(clocks.IsRunning(item));
        clocks.Poll(1500);
        Assert.Equal(10500, clocks.ReadMilliseconds(item));
    }

    [Fact]
    public void ClickingTheCounterStillTogglesAndResetStillStopsIt()
    {
        var item = new Item { Id = 1, Definition = new() { ItemName = "wf_upcounter1" }, ExtraData = new LegacyDataFormat { Data = "10" } };
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        Assert.True(clocks.Use(item, 0, 0));
        Assert.True(clocks.IsRunning(item));
        Assert.True(clocks.Use(item, 0, 100));
        Assert.False(clocks.IsRunning(item));
        Assert.Equal(10000, clocks.ReadMilliseconds(item));
        Assert.True(clocks.Use(item, 0, 200));
        Assert.True(clocks.Use(item, 2, 300));
        Assert.False(clocks.IsRunning(item));
        Assert.Equal(0, clocks.ReadMilliseconds(item));
    }

    [Theory]
    [InlineData("wf_upcounter1", 2, 11999L)]
    [InlineData("bb_counter", 0, 2147483646L)]
    public void RestoredClockStateAndPulseWritesPreserveStartLifecycleAndNativeLimits(string name, int restoredState, long maximum)
    {
        var item = new Item { Id = 1, Definition = new() { ItemName = name }, ExtraData = new LegacyDataFormat { Data = "10" } };
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        Assert.Equal(restoredState, clocks.ReadState(item));
        Assert.True(clocks.SetPulseCount(item, int.MaxValue));
        Assert.Equal(maximum, clocks.ReadPulseCount(item));
        Assert.Equal(restoredState, clocks.ReadState(item));
        Assert.True(clocks.SetPulseCount(item, -1));
        Assert.Equal(0, clocks.ReadPulseCount(item));
        Assert.Equal(restoredState, clocks.ReadState(item));
        Assert.True(clocks.Control(item, 0, 0));
        Assert.Equal(1, clocks.ReadState(item));
        Assert.True(clocks.Control(item, 3, 100));
        Assert.Equal(2, clocks.ReadState(item));
        Assert.True(clocks.Control(item, 2, 200));
        Assert.Equal(0, clocks.ReadState(item));
        clocks.Forget(item);
        Assert.Null(clocks.ReadState(item));
        Assert.False(clocks.SetPulseCount(item, 1));
    }

}
