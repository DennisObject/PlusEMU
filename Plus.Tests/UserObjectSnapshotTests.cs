using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserObjectSnapshotTests
{
    [Fact]
    public void UserObjectRetainsExactFieldsAfterUserAndStatisticsMutation()
    {
        var user = new Habbo { Id = 7, Username = "Alice", Look = "look", Gender = "f", Motto = "motto",
            ChangingName = true, LastOnlineAt = DateTimeOffset.FromUnixTimeSeconds(2200000000),
            HabboStats = new(0, 0, 8, 0, 0, 0, 9, 10, 0, 0, 0, 0, "", 0) };
        var composer = new UserObjectComposer(UserObjectSnapshot.Capture(user));
        object[] expected = [7, "Alice", "look", "F", "motto", "", false, 8, 9, 10, false, "2200000000", true, false];
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);
        Assert.Equal(expected, first.Writes);
        user.Username = "changed";
        user.Gender = "m";
        user.LastOnlineAt = null;
        user.ChangingName = false;
        user.HabboStats.Respect = 99;
        var second = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(second);
        Assert.Equal(expected, second.Writes);
    }

    [Fact]
    public async Task InformationRequestSendsCapturedUserBeforePerks()
    {
        var user = new Habbo { Id = 7, HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0) };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var service = new UserProfileService(null!, null!, null!, null!, null!, TimeProvider.System);
        await new InfoRetrieveEvent(service).Parse(session, HabbiconTestSupport.Incoming());
        Assert.Equal([ServerPacketHeader.UserObjectComposer, ServerPacketHeader.UserPerksComposer], sent.Select(packet => packet.Header));
    }
}
