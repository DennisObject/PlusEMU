using System.Text.Json;
using System.Text.RegularExpressions;
using Plus.Communication.Packets.Incoming.Camera;
using Plus.HabboHotel.Camera;
using Xunit;

namespace Plus.Tests;

public class CameraCheckoutPacketTests
{
    private static readonly Guid MediaId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
    private static readonly Regex ReturnPath = new(
        "^/camera/(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{32})\\.png$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void ReadsOnlyABareMediaIdInNOrDForm()
    {
        Assert.Equal(MediaId, Read(MediaId.ToString("D")));
        Assert.Equal(MediaId, Read(MediaId.ToString("N")));
        Assert.Equal(MediaId, Read(MediaId.ToString("D").ToUpperInvariant()));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming(""), out _));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming(new string('0', 32)), out _));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming("/camera/" + MediaId.ToString("D") + ".png"), out _));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming("http://cdn.example/" + MediaId.ToString("N") + ".png"), out _));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming("{" + MediaId.ToString("D") + "}"), out _));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming(MediaId.ToString("D"), 1), out _));
        Assert.False(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming(), out _));
    }

    [Fact]
    public void StorageUrlIsOnlyTheCameraMediaPath()
    {
        var json = CameraStorageReply.Success("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", MediaId, MediaId, "render");
        var url = JsonDocument.Parse(json).RootElement.GetProperty("url").GetString();

        Assert.Equal(CameraMediaPath.For(MediaId), url);
        Assert.Matches(ReturnPath, url);
        Assert.Equal("", JsonDocument.Parse(CameraStorageReply.Failure("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "render")).RootElement.GetProperty("url").GetString());
    }

    private static Guid Read(string value)
    {
        Assert.True(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming(value), out var id));

        return id;
    }
}
