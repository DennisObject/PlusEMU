using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Xunit;

namespace Plus.Tests;

public sealed class FloodDeadlineTests
{
    private static readonly DateTimeOffset Now = new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void EntryRemainingSecondsAreNonnegativeRoundedUpAndBounded()
    {
        Assert.Equal(0, GetRoomEntryDataEvent.RemainingFloodSeconds(Now, Now.AddTicks(-1)));
        Assert.Equal(0, GetRoomEntryDataEvent.RemainingFloodSeconds(Now, Now));
        Assert.Equal(1, GetRoomEntryDataEvent.RemainingFloodSeconds(Now, Now.AddMilliseconds(1)));
        Assert.Equal(2, GetRoomEntryDataEvent.RemainingFloodSeconds(Now, Now.AddMilliseconds(1001)));
        Assert.Equal(int.MaxValue, GetRoomEntryDataEvent.RemainingFloodSeconds(Now, DateTimeOffset.MaxValue));
    }
}
