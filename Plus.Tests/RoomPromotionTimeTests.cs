using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomPromotionTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(60, 1)]
    [InlineData(60.1, 2)]
    [InlineData(119.9, 2)]
    public void MinutesLeftPreservesCeilingWireBehavior(double seconds, int expected)
    {
        var promotion = Promotion(Now.AddSeconds(seconds));

        Assert.Equal(expected, promotion.MinutesLeft);
    }

    [Fact]
    public void MissingExpiryIsExpiredAndEmitsZeroMinutes()
    {
        var promotion = Promotion(null);

        Assert.True(promotion.HasExpired);
        Assert.Equal(0, promotion.MinutesLeft);
    }

    [Fact]
    public void ExpirationSamplesTheInjectedClock()
    {
        var clock = new FixedClock(Now);
        var promotion = new RoomPromotion("name", "description", Now, Now.AddMinutes(1), 1, clock);
        Assert.False(promotion.HasExpired);

        clock.Now = Now.AddMinutes(1);

        Assert.True(promotion.HasExpired);
        Assert.Equal(0, promotion.MinutesLeft);
    }

    [Fact]
    public void DistantFutureExpiryClampsWireMinutes()
    {
        var promotion = Promotion(DateTimeOffset.MaxValue);
        Assert.Equal(int.MaxValue, promotion.MinutesLeft);
    }

    private static RoomPromotion Promotion(DateTimeOffset? expiresAt) =>
        new("name", "description", Now, expiresAt, 1, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
