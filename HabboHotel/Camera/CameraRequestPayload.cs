namespace Plus.HabboHotel.Camera;

public sealed record CameraRequestPayload(string? Json)
{
    internal CameraRejectReason FrameError { get; init; }
}
