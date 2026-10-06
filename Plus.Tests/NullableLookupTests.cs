using Plus.HabboHotel.Rooms.Chat.Logs;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public class NullableLookupTests
{
    [Fact]
    public void StoredChatCanBeReadWithoutLiveRoomOrPlayer()
    {
        var entry = new ChatlogEntry(12, 34, "hello", DateTimeOffset.UnixEpoch.AddSeconds(123));

        Assert.Null(entry.PlayerNullable());
        Assert.Null(entry.RoomNullable());
        Assert.Equal("hello", entry.Message);
    }

    [Fact]
    public void PathNodeComparesAndChecksEqualityWithNull()
    {
        var node = new PathFinderNode(new Vector2D(1, 2));

        Assert.True(node.CompareTo(null) > 0);
        Assert.False(node.Equals((PathFinderNode?)null));
        Assert.False(node.Equals((object?)null));
        Assert.Null(node.Next);
    }

    [Fact]
    public void HeapKeepsOrderingAcrossGrowthAndInterleavedRemoval()
    {
        var heap = new MinHeap<int>(2);

        foreach (var value in new[] { 7, 2, 6, 1, 4 }) {
            heap.Add(value);
        }

        Assert.Equal(1, heap.ExtractFirst());
        heap.Add(3);
        Assert.Equal(new[] { 2, 3, 4, 6, 7 }, Enumerable.Range(0, 5).Select(_ => heap.ExtractFirst()));
        Assert.Throws<InvalidOperationException>(() => heap.ExtractFirst());
    }
}
