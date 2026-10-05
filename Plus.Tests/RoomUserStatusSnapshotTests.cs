using System.Globalization;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomUserStatusSnapshotTests
{
    [Fact]
    public void StatusWireShapeIsInvariantAndFrozenBeforeComposition()
    {
        var user = new RoomUser(7, 42, 9, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 3, Y = 4, Z = 1.25, RotHead = 2, RotBody = 6 };
        user.Statusses.Add("sit", "0.5");
        var source = new List<RoomUser> { user };
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var data = RoomUserStatusSnapshot.Capture(source);
            var composer = new UserUpdateComposer(data);
            var first = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(first);
            Assert.Equal(new object[] { 1, 9, 3, 4, "1.25", 2, 6, "/sit 0.5//" }, first.Writes);

            user.X = 99;
            user.Z = 99;
            user.RotBody = 0;
            user.Statusses.Clear();
            source.Clear();
            var second = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(second);
            Assert.Equal(first.Writes, second.Writes);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void EmptyStatusRetainsTheTwoSlashesAndEmptyListRetainsTheCount()
    {
        var user = new RoomUser(7, 42, 9, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        Assert.Equal("//", Assert.Single(RoomUserStatusSnapshot.Capture([user])).Status);
        var packet = new HabbiconTestSupport.RecordingPacket();
        new UserUpdateComposer([]).Compose(packet);
        Assert.Equal(new object[] { 0 }, packet.Writes);
    }
}
