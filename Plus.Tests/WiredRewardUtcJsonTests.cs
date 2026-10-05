using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Xunit;

namespace Plus.Tests;

public sealed class WiredRewardUtcJsonTests
{
    [Fact]
    public void ParserAcceptsLegacyFractionalFutureAndUnknownTimes()
    {
        var claims = WiredRewardClaimsJson.Parse("""
            {"1":{"Count":2,"LastClaimUnix":2208988800.125,"ReceivedCodes":["A"]},
             "2":{"Count":1,"LastClaimUnix":0,"ReceivedCodes":[]},
             "3":{"Count":1,"ReceivedCodes":["B"]},
             "4":{"Count":1,"LastClaimUnix":-1,"ReceivedCodes":[]}}
            """);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2208988800).AddMilliseconds(125), claims[1].LastClaimAt);
        Assert.Equal(2, claims[1].Count); Assert.Equal(["A"], claims[1].ReceivedCodes);
        Assert.Null(claims[2].LastClaimAt); Assert.Null(claims[3].LastClaimAt); Assert.Null(claims[4].LastClaimAt);
        Assert.True(WiredRewards.IntervalOpen(claims[2], 3, 1, DateTimeOffset.UnixEpoch));
        Assert.False(WiredRewards.IntervalOpen(claims[2], 0, 1, DateTimeOffset.MaxValue));
        Assert.False(WiredRewards.IntervalOpen(claims[1], 3, 1,
            DateTimeOffset.FromUnixTimeSeconds(2208988800)));
    }

    [Fact]
    public void CanonicalJsonNormalizesUtcAndRoundTripsClaimsAndCodes()
    {
        var parsed = WiredRewardClaimsJson.Parse("""
            {"7":{"Count":3,"LastClaimAt":"2040-01-02T03:04:05.123456+02:00","ReceivedCodes":["A","B"]}}
            """);
        Assert.Equal(TimeSpan.Zero, parsed[7].LastClaimAt!.Value.Offset);

        var json = WiredRewardClaimsJson.Serialize(parsed);
        Assert.Contains("LastClaimAt", json); Assert.DoesNotContain("LastClaimUnix", json);
        var roundTrip = WiredRewardClaimsJson.Parse(json);
        Assert.Equal(parsed[7].LastClaimAt, roundTrip[7].LastClaimAt);
        Assert.Equal(3, roundTrip[7].Count);
        Assert.Equal(new[] { "A", "B" }, roundTrip[7].ReceivedCodes.Order());
    }

    [Fact]
    public void IntervalIsClosedBeforeAndAtFutureClaimsAndOpensAtExactElapsedBoundary()
    {
        var claim = new WiredRewardClaim { Count = 1, LastClaimAt = DateTimeOffset.Parse("2040-01-01T00:00:00Z") };
        Assert.False(WiredRewards.IntervalOpen(claim, 3, 2, claim.LastClaimAt.Value.AddSeconds(119)));
        Assert.True(WiredRewards.IntervalOpen(claim, 3, 2, claim.LastClaimAt.Value.AddSeconds(120)));
        Assert.True(WiredRewards.IntervalOpen(claim, 3, 2, claim.LastClaimAt.Value.AddSeconds(121)));
        Assert.False(WiredRewards.IntervalOpen(claim, 3, 2, claim.LastClaimAt.Value.AddMinutes(-1)));
    }
}
