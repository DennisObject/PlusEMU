using Plus.Communication.Packets.Outgoing.Handshake;
using Xunit;

namespace Plus.Tests;

public class CameraPermissionWireTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CameraPermissionUsesTheOptionalResolvedPermissionBlock(bool allowed)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new UserRightsComposer(7, false, allowed).Compose(packet);

        var expected = new List<object> { 2, 7, false, 7, "", "", "", "", allowed ? 1 : 0 };
        if (allowed) expected.AddRange(["acc_camera", 1]);
        Assert.Equal(expected, packet.Writes);
    }
}
