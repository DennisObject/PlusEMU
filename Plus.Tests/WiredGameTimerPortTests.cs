using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Xunit;

namespace Plus.Tests;

public sealed class WiredGameTimerPortTests
{
    [Theory]
    [InlineData("bb_counter", InteractionType.Banzaicounter)]
    [InlineData("fball_counter", InteractionType.Counter)]
    [InlineData("es_counter", InteractionType.Freezetimer)]
    public void NativeGameTimersCountDownWholeSecondsAndAdjustWithoutTheWiredCounterCeiling(string name, InteractionType type)
    {
        var item = Timer(name, type, "10");
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        Assert.True(clocks.Control(item, 0, 0));
        clocks.Poll(500);
        Assert.Equal(10000, clocks.ReadMilliseconds(item));
        clocks.Poll(1000);
        Assert.Equal(9000, clocks.ReadMilliseconds(item));
        Assert.True(clocks.Adjust(item, 2, 99, 119));
        Assert.Equal(5_999_000, clocks.ReadMilliseconds(item));
        Assert.True(clocks.Adjust(item, 0, 1, 1));
        Assert.Equal(6_059_000, clocks.ReadMilliseconds(item));
        Assert.True(clocks.Control(item, 3, 1500));
        clocks.Poll(2000);
        Assert.Equal(6_059_000, clocks.ReadMilliseconds(item));
        Assert.True(clocks.Control(item, 4, 2000));
        clocks.Poll(3000);
        Assert.Equal(6_058_000, clocks.ReadMilliseconds(item));
        Assert.True(clocks.Control(item, 2, 3000));
        Assert.False(clocks.IsRunning(item));
        Assert.Equal(30000, clocks.ReadMilliseconds(item));
    }

    [Fact]
    public void EndingTheGameStopsNativeTimersWithoutDiscardingTheirRemainingValue()
    {
        var item = Timer("bb_counter", InteractionType.Banzaicounter, "10");
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        clocks.Control(item, 0, 0);
        clocks.Poll(1000);
        clocks.OnGameEnded();
        clocks.Poll(2000);
        Assert.False(clocks.IsRunning(item));
        Assert.Equal(9000, clocks.ReadMilliseconds(item));
    }

    private static Item Timer(string name, InteractionType type, string state) => new()
    {
        Id = 1,
        Definition = new() { ItemName = name, InteractionName = "game_timer", InteractionType = type },
        ExtraData = new LegacyDataFormat { Data = state }
    };
}
