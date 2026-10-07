using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;

namespace Plus.Tests.SnowStorm.Simulation;

public class SnowStormMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(100, 10)]
    [InlineData(255, 15)]
    [InlineData(256, 16)]
    [InlineData(1000, 31)]
    [InlineData(4096, 64)]
    [InlineData(10000, 99)]
    [InlineData(25600, 160)]
    [InlineData(65536, 256)]
    [InlineData(160000, 398)]
    [InlineData(1000000, 996)]
    [InlineData(int.MaxValue, 46080)]
    [InlineData(-1, -1)]
    public void FastSqrtMatchesTheAirTable(int value, int expected)
    {
        Assert.Equal(expected, SnowStormMath.FastSqrt(value));
    }

    [Fact]
    public void FastSqrtStaysCloseToTheRealRoot()
    {
        for (var value = 1; value < 1_000_000_000; value = value * 3 / 2 + 1) {
            double root = Math.Sqrt(value);
            Assert.InRange(SnowStormMath.FastSqrt(value), root * 0.98 - 1, root * 1.01 + 1);
        }
    }

    [Theory]
    [InlineData(7.9, 7)]
    [InlineData(-7.9, -7)]
    [InlineData(0.5, 0)]
    [InlineData(-0.5, 0)]
    [InlineData(-3.0, -3)]
    public void JavaDivTruncatesTowardZero(double value, int expected)
    {
        Assert.Equal(expected, SnowStormMath.JavaDiv(value));
    }

    [Theory]
    [InlineData(0, 253983)]
    [InlineData(1, 270369)]
    [InlineData(2, 540738)]
    [InlineData(100, 27036706)]
    [InlineData(-5, 1269915)]
    public void IterateSeedIsTheAirXorShift(int seed, int expected)
    {
        Assert.Equal(expected, SnowStormMath.IterateSeed(seed));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(21, 0)]
    [InlineData(22, 1)]
    [InlineData(66, 1)]
    [InlineData(67, 2)]
    [InlineData(90, 2)]
    [InlineData(180, 4)]
    [InlineData(270, 6)]
    [InlineData(336, 7)]
    [InlineData(337, 0)]
    [InlineData(360, 0)]
    [InlineData(-90, 6)]
    public void Direction360QuantisesWithTheAirOffset(int direction360, int expected)
    {
        Assert.Equal(expected, SnowStormMath.Direction360ToDirection8(direction360));
    }

    [Theory]
    [InlineData(0, -1, 360)]
    [InlineData(1, -1, 45)]
    [InlineData(1, 0, 90)]
    [InlineData(1, 1, 135)]
    [InlineData(0, 1, 180)]
    [InlineData(-1, 1, 225)]
    [InlineData(-1, 0, 270)]
    [InlineData(-1, -1, 315)]
    [InlineData(0, 0, 180)]
    [InlineData(160, 0, 90)]
    [InlineData(100, -50, 63)]
    public void AngleFromComponentsUsesTheAtanTable(int x, int y, int expected)
    {
        Assert.Equal(expected, SnowStormMath.GetAngleFromComponents(x, y));
    }

    [Fact]
    public void BaseVectorsPointNorthAtZeroAndEastAtNinety()
    {
        Assert.Equal((0, -256), (SnowStormMath.BaseVectorXComponent(0), SnowStormMath.BaseVectorYComponent(0)));
        Assert.Equal((256, 0), (SnowStormMath.BaseVectorXComponent(90), SnowStormMath.BaseVectorYComponent(90)));
        Assert.Equal((181, 181), (SnowStormMath.BaseVectorXComponent(135), SnowStormMath.BaseVectorYComponent(135)));
        Assert.Equal(SnowStormMath.BaseVectorXComponent(10), SnowStormMath.BaseVectorXComponent(370));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1599, 0)]
    [InlineData(1600, 1)]
    [InlineData(4799, 1)]
    [InlineData(-1600, 0)]
    [InlineData(-4800, -1)]
    public void WorldToTileRoundsToTheNearestTileCentre(int world, int expected)
    {
        Assert.Equal(expected, SnowStormMath.WorldToTile(world));
    }

    [Fact]
    public void DistanceTestIsStrict()
    {
        Assert.True(SnowStormMath.IsInDistance(0, 0, 1999, 0, 2000));
        Assert.False(SnowStormMath.IsInDistance(0, 0, 2000, 0, 2000));
        Assert.False(SnowStormMath.IsInDistance(0, 0, 1500, 1500, 2000));
    }
}
