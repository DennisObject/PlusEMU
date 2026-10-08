using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Camera;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Camera;
using Xunit;

namespace Plus.Tests;

public class CameraRequestTests
{
    private const string RequestId = "11111111-1111-4111-8111-111111111111";
    private const string DraftId = "22222222-2222-4222-8222-222222222222";

    private static readonly Dictionary<string, int> Catalogue = new(StringComparer.Ordinal)
    {
        ["dark_sepia"] = 0,
        ["Yellow"] = 6
    };

    [Fact]
    public void CameraSectionKeepsBearerAndEffectCase()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Camera:RendererUrl"] = "http://127.0.0.1:3921/render",
            ["Camera:Bearer"] = "SecretToken",
            ["Camera:OutputDirectory"] = "camera",
            ["Camera:TimeoutSeconds"] = "15",
            ["Camera:MaxConcurrency"] = "2",
            ["Camera:Effects:0:Name"] = "Yellow",
            ["Camera:Effects:0:MinLevel"] = "6"
        }).Build();
        var services = new ServiceCollection();
        Program.AddConfiguration<CameraConfiguration>(services, config.GetSection("Camera"));
        var camera = services.BuildServiceProvider().GetRequiredService<IOptions<CameraConfiguration>>().Value;

        Assert.Equal("SecretToken", camera.Bearer);
        Assert.Equal("http://127.0.0.1:3921/render", camera.RendererUrl);
        Assert.Equal("Yellow", Assert.Single(camera.Effects).Name);
        Assert.Equal(6, camera.Effects[0].MinLevel);
        Assert.Equal("", new CameraConfiguration().Bearer);
    }

    [Fact]
    public void AcceptsPhotoCaptureAt320AndThumbnailCaptureAt110()
    {
        var photo = Parse(Capture(320), CameraChannel.Photo);
        var thumb = Parse(Capture(110), CameraChannel.Thumbnail);
        var photoCommand = Assert.IsType<CameraCommand.Capture>(photo.Command);
        var thumbCommand = Assert.IsType<CameraCommand.Capture>(thumb.Command);

        Assert.Equal(CameraParseStatus.Accepted, photo.Status);
        Assert.Equal(RequestId, photo.RequestId);
        Assert.Equal("capture", photo.Stage);
        Assert.Equal(320, photoCommand.Viewport.CropWidth);
        Assert.Equal(320, photoCommand.Viewport.CropHeight);
        Assert.Equal(1d, photoCommand.Viewport.Scale);
        Assert.Equal(10.5d, photoCommand.Viewport.LocationX);
        Assert.Equal(CameraParseStatus.Accepted, thumb.Status);
        Assert.Equal(110, thumbCommand.Viewport.CropWidth);
        Assert.Equal(110, thumbCommand.Viewport.CropHeight);
    }

    [Fact]
    public void AcceptsRenderWithAllowlistedEffectAndDeleteOfOwnedGuid()
    {
        var render = Parse(Render("""[{"name":"Yellow","strength":0.5}]""", "false"), level: 6);
        var empty = Parse(Render("[]", "true"));
        var delete = Parse(Delete());
        var command = Assert.IsType<CameraCommand.Render>(render.Command);

        Assert.Equal(CameraParseStatus.Accepted, render.Status);
        Assert.Equal(RequestId, command.RequestId);
        Assert.Equal(DraftId, command.DraftId);
        Assert.False(command.Zoom);
        Assert.Equal(("Yellow", 0.5d), (command.Effects.Single().Name, command.Effects.Single().Strength));
        Assert.Empty(Assert.IsType<CameraCommand.Render>(empty.Command).Effects);
        Assert.True(Assert.IsType<CameraCommand.Render>(empty.Command).Zoom);
        Assert.Equal(DraftId, Assert.IsType<CameraCommand.Delete>(delete.Command).DraftId);
        Assert.Equal("", delete.RequestId);
    }

    [Theory]
    [InlineData("pixels")]
    [InlineData("sprite")]
    public void RejectsPixelFields(string field)
    {
        var result = Parse(Capture(320, $"\"{field}\":\"iVBORw0KGgo\""));
        Assert.Equal(CameraParseStatus.Rejected, result.Status);
        Assert.Equal(CameraRejectReason.Pixels, result.Reason);
        Assert.Equal(RequestId, result.RequestId);
    }

    [Fact]
    public void RejectsLegacyPngBytes()
    {
        var result = ParsePacket(new FlashIncomingPacket
        {
            Buffer = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }
        }, CameraChannel.Photo, Catalogue, 6);

        Assert.Equal(CameraParseStatus.Malformed, result.Status);
        Assert.Equal(CameraRejectReason.Pixels, result.Reason);
        Assert.Equal("", result.RequestId);
    }

    [Fact]
    public void RejectsUrlsUnknownFieldsAndNonFiniteNumbers()
    {
        var url = Parse(Render("""[{"name":"https://evil.example/a.png","strength":1}]"""));
        var protocol = Parse(Render("""[{"name":"javascript:alert(1)","strength":1}]"""));
        var relative = Parse(Render("""[{"name":"//cdn.example/a.png","strength":1}]"""));
        var unknown = Parse(Capture(320, "\"floor\":\"x\""));
        var infinite = Parse(Capture(320).Replace("\"locationX\":10.5", "\"locationX\":1e9999", StringComparison.Ordinal));

        Assert.Equal((CameraParseStatus.Rejected, CameraRejectReason.Url, RequestId), (url.Status, url.Reason, url.RequestId));
        Assert.Equal(CameraRejectReason.Url, protocol.Reason);
        Assert.Equal(CameraRejectReason.Url, relative.Reason);
        Assert.Equal(CameraRejectReason.UnknownProperty, unknown.Reason);
        Assert.Equal(CameraRejectReason.NonFinite, infinite.Reason);
        Assert.Equal("capture", infinite.Stage);
    }

    [Theory]
    [InlineData(CameraChannel.Photo, 110)]
    [InlineData(CameraChannel.Photo, 319)]
    [InlineData(CameraChannel.Thumbnail, 320)]
    [InlineData(CameraChannel.Thumbnail, 111)]
    public void RejectsCropMismatch(CameraChannel channel, int crop)
    {
        var result = Parse(Capture(crop), channel);
        Assert.Equal(CameraParseStatus.Rejected, result.Status);
        Assert.Equal(CameraRejectReason.Crop, result.Reason);
        Assert.Equal(RequestId, result.RequestId);
        Assert.Equal("capture", result.Stage);
    }

    [Fact]
    public void RejectsDuplicateKeysDuplicateEffectsAndInvalidEffects()
    {
        var duplicateKey = Parse(Capture(320).Replace("\"v\":1", "\"v\":1,\"v\":1", StringComparison.Ordinal));
        var duplicateEffect = Parse(Render("""[{"name":"dark_sepia","strength":1},{"name":"dark_sepia","strength":0}]"""));
        var unknownEffect = Parse(Render("""[{"name":"not_real","strength":1}]"""));
        var weakLevel = Parse(Render("""[{"name":"Yellow","strength":1}]"""), level: 5);
        var strong = Parse(Render("""[{"name":"dark_sepia","strength":1.1}]"""));
        var typed = Parse(Render("""[{"name":"dark_sepia","strength":"1"}]"""));
        var framed = Parse(Render("""[{"name":"dark_sepia","strength":1,"frame":2}]"""));
        var zoom = Parse(Render("[]", "1"));
        var ninth = Parse(Render("[" + string.Join(',', Enumerable.Repeat("{\"name\":\"dark_sepia\",\"strength\":0}", 9)) + "]"));

        Assert.Equal(CameraRejectReason.Duplicate, duplicateKey.Reason);
        Assert.Equal(CameraRejectReason.Duplicate, duplicateEffect.Reason);
        Assert.Equal(CameraRejectReason.Effect, unknownEffect.Reason);
        Assert.Equal(CameraRejectReason.Effect, weakLevel.Reason);
        Assert.Equal(CameraRejectReason.Effect, strong.Reason);
        Assert.Equal(CameraRejectReason.Effect, typed.Reason);
        Assert.Equal(CameraRejectReason.UnknownProperty, framed.Reason);
        Assert.Equal(CameraRejectReason.Schema, zoom.Reason);
        Assert.Equal(CameraRejectReason.Effect, ninth.Reason);
        Assert.Equal(RequestId, duplicateKey.RequestId);
        Assert.Equal("render", duplicateEffect.Stage);
    }

    [Fact]
    public void RejectsBadGuidsOversizedInputAndTrailingBytes()
    {
        var badRequest = Parse(Capture(320).Replace(RequestId, "not-a-guid", StringComparison.Ordinal));
        var bareGuid = Parse(Capture(320).Replace(RequestId, "11111111111141118111111111111111", StringComparison.Ordinal));
        var badDraft = Parse(Render("[]").Replace(DraftId, "forged", StringComparison.Ordinal));
        var badDelete = Parse(Delete().Replace(DraftId, "{22222222-2222-4222-8222-222222222222}", StringComparison.Ordinal));
        var oversized = ParsePacket(Framed(new byte[8193]), CameraChannel.Photo, Catalogue, 6);
        var trailing = ParsePacket(HabbiconTestSupport.Incoming(Capture(320), 1), CameraChannel.Photo, Catalogue, 6);
        var comment = Parse("{\"v\":1 /*no*/}");

        Assert.Equal((CameraParseStatus.Malformed, CameraRejectReason.Guid, ""), (badRequest.Status, badRequest.Reason, badRequest.RequestId));
        Assert.Equal(CameraParseStatus.Malformed, bareGuid.Status);
        Assert.Equal((CameraParseStatus.Rejected, CameraRejectReason.Guid, RequestId), (badDraft.Status, badDraft.Reason, badDraft.RequestId));
        Assert.Equal((CameraParseStatus.Malformed, CameraRejectReason.Guid), (badDelete.Status, badDelete.Reason));
        Assert.Equal((CameraParseStatus.Malformed, CameraRejectReason.Oversized), (oversized.Status, oversized.Reason));
        Assert.Equal((CameraParseStatus.Malformed, CameraRejectReason.Trailing, ""), (trailing.Status, trailing.Reason, trailing.RequestId));
        Assert.Equal(CameraParseStatus.Malformed, comment.Status);
    }

    [Fact]
    public void EchoesTheClientRequestIdExactlyAndIgnoresNoDeleteReplyId()
    {
        var id = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
        var photo = Parse(Capture(320).Replace(RequestId, id, StringComparison.Ordinal));
        var withRequest = Parse(Delete().Replace("{", "{\"requestId\":\"" + id + "\",", StringComparison.Ordinal));

        Assert.Equal(id, Assert.IsType<CameraCommand.Capture>(photo.Command).RequestId);
        Assert.Equal(CameraParseStatus.Malformed, withRequest.Status);
        Assert.Equal("", withRequest.RequestId);
    }

    [Fact]
    public void MissingVersionReturnsCorrelatedRejectionInsteadOfThrowing()
    {
        var result = Parse(Capture(320).Replace("\"v\":1,", "", StringComparison.Ordinal));
        Assert.Equal(CameraParseStatus.Rejected, result.Status);
        Assert.Equal(RequestId, result.RequestId);
        Assert.Equal("capture", result.Stage);
    }

    private static CameraParseResult Parse(string json, CameraChannel channel = CameraChannel.Photo, int level = 6) =>
        ParsePacket(HabbiconTestSupport.Incoming(json), channel, Catalogue, level);

    private static CameraParseResult ParsePacket(IIncomingPacket packet, CameraChannel channel,
        IReadOnlyDictionary<string, int> catalogue, int level) =>
        CameraRequestParser.Parse(CameraPacketDecoder.Decode(packet), channel, catalogue, level);

    [Fact]
    public async Task RenderHandlersDecodeImmutablePayloadBeforeCallingService()
    {
        var service = new RecordingCamera();
        var photo = HabbiconTestSupport.Incoming(Capture(320));
        var source = photo.Buffer;
        await new RenderRoomEvent(service).Parse(null!, photo);
        await new RenderRoomThumbnailEvent(service).Parse(null!, HabbiconTestSupport.Incoming(Capture(110)));
        Assert.Equal(Capture(320), service.Requests[0].Payload.Json);
        Assert.False(service.Requests[0].Thumbnail);
        Assert.Equal(Capture(110), service.Requests[1].Payload.Json);
        Assert.True(service.Requests[1].Thumbnail);
        source.Span.Clear();
        Assert.Equal(Capture(320), service.Requests[0].Payload.Json);
        Assert.Equal(CameraRejectReason.None, service.Requests[0].Payload.FrameError);
    }

    private sealed class RecordingCamera : ICameraService
    {
        public List<(CameraRequestPayload Payload, bool Thumbnail)> Requests { get; } = [];
        public Task Handle(GameClient session, CameraRequestPayload payload, bool thumbnail)
        {
            Requests.Add((payload, thumbnail));

            return Task.CompletedTask;
        }
        public CameraCheckoutResult Checkout(GameClient session, Guid mediaId,
            Func<CameraCheckoutMedia, CameraCheckoutResult> operation) => throw new InvalidOperationException();
        public void Prepare(GameClient session) { }
    }

    private static string Capture(int crop, string extra = "") =>
        "{\"v\":1,\"action\":\"capture\",\"requestId\":\"" + RequestId + "\"" + (extra.Length == 0 ? "" : "," + extra) +
        ",\"viewport\":{\"width\":1280,\"height\":900,\"offsetX\":-1.5,\"offsetY\":2,\"x\":320,\"y\":200,\"cropWidth\":" + crop +
        ",\"cropHeight\":" + crop + ",\"scale\":1,\"locationX\":10.5,\"locationY\":-4,\"locationZ\":0}}";

    private static string Render(string effects, string zoom = "false") =>
        "{\"v\":1,\"action\":\"render\",\"requestId\":\"" + RequestId + "\",\"draftId\":\"" + DraftId + "\",\"effects\":" + effects + ",\"zoom\":" + zoom + "}";

    private static string Delete() =>
        "{\"v\":1,\"action\":\"delete\",\"draftId\":\"" + DraftId + "\"}";

    private static FlashIncomingPacket Framed(byte[] payload)
    {
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)payload.Length);

        return new FlashIncomingPacket { Buffer = length.Concat(payload).ToArray() };
    }
}
