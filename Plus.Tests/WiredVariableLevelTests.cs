using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableLevelTests
{
    [Fact]
    public void LinearLevelProgressAndFxShareCumulativeThresholds()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":1,\"stepSize\":100,\"maxLevel\":4}", out var system));
        Assert.Equal(new[] { 2, 150, 50, 50, 200, 50, 0, 4 }, Enumerable.Range(0, 8).Select(i => system!.Read(150, i)));
        Assert.Equal(100, system!.Level(150).Start); Assert.Equal(200, system.Level(150).Next);
        Assert.True(system.Level(999).IsMaxed); Assert.Equal(100, system.Read(999, 3)); Assert.Equal(0, system.Read(999, 5));
        Assert.Equal(0, system.Read(-1, 1));
    }
    [Fact]
    public void ExponentialAndInterpolatedThresholdsAreBoundedAndUseJavaRounding()
    {
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":2,\"firstLevelXp\":1,\"increaseFactor\":50,\"maxLevel\":4}", out var exponential));
        Assert.Equal(3, exponential!.Level(3).Level); Assert.Equal(6, exponential.Level(3).Next);
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":3,\"interpolationText\":\"3=5\",\"subvariables\":[0,7]}", out var manual));
        Assert.Equal(3, manual!.Level(0).Next); Assert.Equal(3, manual.Level(5).Level); Assert.Equal(129, manual.SubvariableMask);
        Assert.True(WiredVariableLevelSystem.TryParse("{\"mode\":1,\"maxLevel\":2147483647,\"stepSize\":2147483647}", out var bounded));
        Assert.Equal(10000, bounded!.Level(0).MaxLevel); Assert.Equal(int.MaxValue, bounded.Level(0).Next);
        Assert.False(WiredVariableLevelSystem.TryParse("{\"mode\":3,\"interpolationText\":\"2147483647=1\"}", out _));
    }
}
