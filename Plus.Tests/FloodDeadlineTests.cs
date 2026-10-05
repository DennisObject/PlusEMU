using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class FloodDeadlineTests
{
    private static readonly DateTimeOffset Now = new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void EntryRemainingSecondsAreNonnegativeRoundedUpAndBounded()
    {
        Assert.Equal(0, RoomEntryService.RemainingFloodSeconds(Now, Now.AddTicks(-1)));
        Assert.Equal(0, RoomEntryService.RemainingFloodSeconds(Now, Now));
        Assert.Equal(1, RoomEntryService.RemainingFloodSeconds(Now, Now.AddMilliseconds(1)));
        Assert.Equal(2, RoomEntryService.RemainingFloodSeconds(Now, Now.AddMilliseconds(1001)));
        Assert.Equal(int.MaxValue, RoomEntryService.RemainingFloodSeconds(Now, DateTimeOffset.MaxValue));
    }
}
