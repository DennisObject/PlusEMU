using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableLevelTests
{
    [Fact]
    public void ComputedLevelThresholdsRemainWideAndManualInterpolationKeepsExactIntegers()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":1,\"stepSize\":500000,\"maxLevel\":10000}", out var linear));
        Assert.Equal(4_999_500_000L, linear!.Level(4_999_500_000L).Start);
        Assert.Equal(9999, linear.Level(4_999_000_000L).Level);
        Assert.Equal(4_999_500_000L, linear.Read(4_999_000_000L, 4));
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":3,\"interpolationText\":\"1=9007199254740993\\n3=9007199254740995\"}", out var manual));
        Assert.Equal(9007199254740994L, manual!.Level(9007199254740994L).Start);
        Assert.Equal(2, manual.Level(9007199254740994L).Level);
        Assert.Equal(9007199254740995L, manual.Level(9007199254740994L).Next);
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":2,\"firstLevelXp\":2147483647,\"increaseFactor\":0,\"maxLevel\":3}", out var exponential));
        Assert.Equal(4_294_967_294L, exponential!.Level(4_294_967_294L).Start);
    }

    [Fact]
    public void ExponentialThresholdsUseExactRoundedIncrementsAndSaturateAtSignedMaximum()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":2,\"firstLevelXp\":2147483647,\"increaseFactor\":2147483647,\"maxLevel\":6}", out var system));
        Assert.Equal(2147483647L, system!.Level(2147483647L).Start);
        Assert.Equal(46116864436291500L, system.Level(2147483647L).Next);
        Assert.Equal(long.MaxValue, system.Level(46116864436291500L).Next);
        Assert.Equal(long.MaxValue, system.Level(long.MaxValue).Start);
        Assert.Equal(6, system.Level(long.MaxValue).Level);
    }

    [Fact]
    public void ExplicitZeroAndNegativeInputsFollowActiveEditorNormalization()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":2,\"firstLevelXp\":100,\"increaseFactor\":0,\"maxLevel\":3}", out var flatGrowth));
        Assert.Equal(100, flatGrowth!.Level(150).Start);
        Assert.Equal(200, flatGrowth.Level(150).Next);

        foreach (var zero in new[] { "{\"mode\":1,\"stepSize\":0}", "{\"mode\":2,\"firstLevelXp\":0}",
            "{\"mode\":1,\"stepSize\":-10}", "{\"mode\":2,\"firstLevelXp\":-10}" }) {
            Assert.True(WiredVariableLevelSystem.TryParse(zero, out var system));
            Assert.True(system!.Level(0).IsMaxed);
            Assert.Equal(0, system.Level(0).Next);
        }

        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":2,\"firstLevelXp\":100,\"increaseFactor\":-1,\"maxLevel\":3}", out var negativeGrowth));
        Assert.Equal(200, negativeGrowth!.Level(150).Next);
        Assert.True(WiredVariableLevelSystem.TryParse("{\"maxLevel\":0}", out var zeroLevels));
        Assert.Equal(1, zeroLevels!.Level(0).MaxLevel);
    }
    [Fact]
    public void LinearLevelProgressAndFxShareCumulativeThresholds()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":1,\"stepSize\":100,\"maxLevel\":4}", out var system));
        Assert.Equal(new long[] { 2, 150, 50, 50, 200, 50, 0, 4 }, Enumerable.Range(0, 8).Select(i => system!.Read(150, i)));
        Assert.Equal(100, system!.Level(150).Start);
        Assert.Equal(200, system.Level(150).Next);
        Assert.True(system.Level(999).IsMaxed);
        Assert.Equal(100, system.Read(999, 3));
        Assert.Equal(0, system.Read(999, 5));
        Assert.Equal(0, system.Read(-1, 1));
    }
    [Fact]
    public void ExponentialAndInterpolatedThresholdsAreBoundedAndUseJavaRounding()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":2,\"firstLevelXp\":1,\"increaseFactor\":50,\"maxLevel\":4}", out var exponential));
        Assert.Equal(3, exponential!.Level(3).Level);
        Assert.Equal(6, exponential.Level(3).Next);
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":3,\"interpolationText\":\"3=5\",\"subvariables\":[0,7]}", out var manual));
        Assert.Equal(3, manual!.Level(0).Next);
        Assert.Equal(3, manual.Level(5).Level);
        Assert.Equal(129, manual.SubvariableMask);
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":1,\"maxLevel\":2147483647,\"stepSize\":2147483647}", out var bounded));
        Assert.Equal(10000, bounded!.Level(0).MaxLevel);
        Assert.Equal(int.MaxValue, bounded.Level(0).Next);
        Assert.False(WiredVariableLevelSystem.TryParse("{\"mode\":3,\"interpolationText\":\"2147483647=1\"}", out _));
    }
}
