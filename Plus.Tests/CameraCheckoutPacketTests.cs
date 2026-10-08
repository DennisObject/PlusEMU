using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Camera;
using Plus.Communication.Packets.Outgoing.Camera;
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

    [Fact]
    public void StoredPhotoFollowsTheReplyWithItsLength()
    {
        // Larger than a signed 16-bit string length, which is why the photo is not a string field.
        var png = new byte[40 * 1024];
        new Random(7).NextBytes(png);
        var json = CameraStorageReply.Success("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", MediaId, MediaId, "capture", png.Length);
        var body = Encode(new CameraStorageUrlComposer(json, png));
        var length = BinaryPrimitives.ReadUInt16BigEndian(body);
        var reply = JsonDocument.Parse(Encoding.UTF8.GetString(body, 2, length)).RootElement;

        Assert.Equal(CameraMediaPath.For(MediaId), reply.GetProperty("url").GetString());
        Assert.Equal(png.Length, reply.GetProperty("inline").GetInt32());
        Assert.Equal(png.Length, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(2 + length)));
        Assert.Equal(png, body[(2 + length + 4)..]);
    }

    [Fact]
    public void ReplyWithoutAPhotoIsOnlyTheJson()
    {
        var json = CameraStorageReply.Success("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", MediaId, MediaId, "capture");
        var body = Encode(new CameraStorageUrlComposer(json));

        Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("inline", out _));
        Assert.Equal(2 + Encoding.UTF8.GetByteCount(json), body.Length);
    }

    private static byte[] Encode(CameraStorageUrlComposer composer)
    {
        using var stream = (RecyclableMemoryStream)new RecyclableMemoryStreamManager().GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));

        return stream.ToArray()[6..];
    }

    private static Guid Read(string value)
    {
        Assert.True(CameraCheckoutPacket.TryReadMediaId(HabbiconTestSupport.Incoming(value), out var id));

        return id;
    }
}
