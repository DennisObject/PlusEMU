using System.Text.Json;

namespace Plus.HabboHotel.Camera;

internal static class CameraStorageReply
{
    public static string Success(string requestId, Guid draftId, Guid mediaId, string stage) =>
        JsonSerializer.Serialize(new
        {
            v = 1,
            requestId,
            draftId = draftId.ToString("D"),
            url = CameraMediaPath.For(mediaId),
            stage
        });

    public static string Failure(string requestId, string stage) =>
        JsonSerializer.Serialize(new { v = 1, requestId, stage, url = "" });
}
