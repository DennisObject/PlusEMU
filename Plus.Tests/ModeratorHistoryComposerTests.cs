using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.Moderation;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorHistoryComposerTests
{
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void RoomChatlogPreservesIdentityMessageAndBlankFallback()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new ModeratorRoomChatlogComposer(new(new(42, "HQ"),
            [new(7, "Alice", "hello", Timestamp), new(8, "Bob", "", Timestamp)])).Compose(packet);

        Assert.Equal("HQ", packet.Writes[4]);
        Assert.Equal((uint)42, packet.Writes[7]);
        Assert.Equal((short)2, packet.Writes[8]);
        Assert.Equal(7, packet.Writes[10]);
        Assert.Equal("Alice", packet.Writes[11]);
        Assert.Equal("hello", packet.Writes[12]);
        Assert.Equal("** user sent a blank message **", packet.Writes[17]);
    }

    [Fact]
    public void UserChatlogMarksOnlyTargetMessages()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new ModeratorUserChatlogComposer(new(new(7, "Alice"),
            [new(new(42, "HQ"), [new(7, "Alice", "mine", Timestamp), new(8, "Bob", "other", Timestamp)])])).Compose(packet);

        Assert.Equal(7, packet.Writes[0]);
        Assert.Equal("Alice", packet.Writes[1]);
        Assert.Equal(true, packet.Writes[16]);
        Assert.Equal(false, packet.Writes[21]);
    }

    [Fact]
    public void UserRoomVisitsPreserveRoomAndUtcClockFields()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new ModeratorUserRoomVisitsComposer(new(new(7, "Alice"),
            [new(new(42, "HQ"), Timestamp)])).Compose(packet);

        Assert.Equal(new object[] { 7, "Alice", 1, (uint)42, "HQ", Timestamp.Hour, Timestamp.Minute }, packet.Writes);
    }

    [Fact]
    public void NestedHistoryCollectionsCannotMutateComposerOutput()
    {
        ModeratorRoomChatlog history = new(new(42, "HQ"), [new(7, "Alice", "original", Timestamp)]);
        var composer = new ModeratorRoomChatlogComposer(history);
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);

        var changed = history.Entries.Add(new(8, "Bob", "later", Timestamp));
        var second = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(second);

        Assert.Equal(2, changed.Length);
        Assert.Single(history.Entries);
        Assert.Equal(first.Writes, second.Writes);
    }
}
