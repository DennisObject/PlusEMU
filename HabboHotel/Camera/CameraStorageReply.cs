using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Camera;

internal static class CameraStorageReply
{
    // Photos up to this size follow the reply in the same packet; larger ones are loaded from their URL.
    public const int MaxInlinePng = 512 * 1024;
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Success(string requestId, Guid draftId, Guid mediaId, string stage, int inline = 0) =>
        JsonSerializer.Serialize(new
        {
            v = 1,
            requestId,
            draftId = draftId.ToString("D"),
            url = CameraMediaPath.For(mediaId),
            stage,
            inline = inline > 0 ? inline : (int?)null
        }, Json);

    public static string Failure(string requestId, string stage) =>
        JsonSerializer.Serialize(new { v = 1, requestId, stage, url = "" });
}
