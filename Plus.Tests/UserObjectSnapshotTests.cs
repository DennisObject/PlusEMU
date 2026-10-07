using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Core.Settings;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserObjectSnapshotTests
{
    [Fact]
    public void UserObjectRetainsExactFieldsAfterUserAndStatisticsMutation()
    {
        var user = new Habbo
        {
            Id = 7,
            Username = "Alice",
            Look = "look",
            Gender = "f",
            Motto = "motto",
            ChangingName = true,
            LastOnlineAt = DateTimeOffset.FromUnixTimeSeconds(2200000000),
            HabboStats = new(0, 0, 8, 0, 0, 0, 9, 10, 0, 0, 0, 0, "", 0)
        };
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
        var service = new UserProfileService(null!, null!, null!, null!, null!, TimeProvider.System, null!, null!, null!, new CameraSettings("0"));
        await new InfoRetrieveEvent(service).Parse(session, HabbiconTestSupport.Incoming());
        Assert.Equal([ServerPacketHeader.UserObjectComposer, ServerPacketHeader.UserPerksComposer], sent.Select(packet => packet.Header));
    }

    [Theory]
    [InlineData("1", true, true)]
    [InlineData("1", false, false)]
    [InlineData("0", true, false)]
    public void PerksAllowTheToolbarCameraOnlyWhenTheUserMayUseIt(string cameraEnabled, bool canUseCamera, bool expected)
    {
        var user = new Habbo { Id = 7, HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0),
            Access = EditorTestSupport.Access(canUseCamera ? [PermissionKeys.CameraUse] : []) };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var service = new UserProfileService(null!, null!, null!, null!, null!, TimeProvider.System, null!, null!, null!, new CameraSettings(cameraEnabled));

        service.ShowUserObject(session);

        // The CAMERA perk is written as its name, an empty requirement and the allowed flag.
        var perks = sent.Single(packet => packet.Header == ServerPacketHeader.UserPerksComposer).Payload;
        byte[] camera = [0, 6, .."CAMERA"u8, 0, 0];
        var index = perks.AsSpan().IndexOf(camera);
        Assert.True(index >= 0);
        Assert.Equal(expected, perks[index + camera.Length] == 1);
    }

    private sealed class CameraSettings(string cameraEnabled) : ISettingsManager
    {
        public string TryGetValue(string value) => value == "camera.enabled" ? cameraEnabled : "";
        public string? GetOptionalValue(string key) => null;
        public Task Reload() => Task.CompletedTask;
    }
}
