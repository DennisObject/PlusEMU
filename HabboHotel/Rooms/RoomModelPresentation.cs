namespace Plus.HabboHotel.Rooms;

public sealed record RoomModelPresentation(bool HalfScale, int CameraX, int CameraY, float CameraZ)
{
    public static RoomModelPresentation Default { get; } = new(true, 0, 0, 0);
    public int Scale => HalfScale ? 32 : 64;
}
