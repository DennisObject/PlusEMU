using Plus.HabboHotel.Quests;
using Xunit;

namespace Plus.Tests;

public class RewardTrackWindowTests
{
    [Fact]
    public void StartIsInclusiveAndEndIsExclusiveAtANonUtcOffset()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(5));
        var end = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.FromHours(5));
        var track = new RewardTrack("window", "blue", 1, start, end, false, 0, 0, 0, 0);

        Assert.Equal(TimeSpan.Zero, track.StartsAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, track.EndsAt!.Value.Offset);
        Assert.False(track.IsActiveAt(start.AddTicks(-1)));
        Assert.True(track.IsActiveAt(start));
        Assert.True(track.IsActiveAt(end.AddTicks(-1)));
        Assert.False(track.IsActiveAt(end));
    }

    [Fact]
    public void MissingBoundsAreUnboundedOnTheirSide()
    {
        var unbounded = new RewardTrack("introduction", "blue", 0, null, null, true, 1.5, 25, 0, 25);
        var openEnd = new RewardTrack("open", "blue", 1, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null, false, 0, 0, 0, 0);

        Assert.True(unbounded.IsActiveAt(new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(unbounded.IsActiveAt(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(openEnd.IsActiveAt(new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero)));
        Assert.True(openEnd.IsActiveAt(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
