using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomEventSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposerPreservesActiveAndAbsentPromotionFields(bool active)
    {
        var room = new RoomData { Id = 42, OwnerId = 7, OwnerName = "Alice" };
        var promotion = active ? new RoomPromotion("Event", "Description", 1,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), TimeProvider.System) : null;
        var composer = new RoomEventComposer(RoomEventSnapshot.Capture(room, promotion));
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        Assert.Equal(new object[] { active ? 42 : -1, active ? 7 : -1, active ? "Alice" : "", active ? 1 : 0,
            0, active ? "Event" : "", active ? "Description" : "", 0, 0, 0 }, packet.Writes);

        room.Id = 99;
        room.OwnerName = "Changed";

        if (promotion != null) {
            promotion.Name = "Changed";
            promotion.Description = "Changed";
        }

        var repeated = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(repeated);
        Assert.Equal(packet.Writes, repeated.Writes);
    }
}
