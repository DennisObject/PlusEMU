using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

internal static class CameraCheckoutPacket
{
    public static CameraCheckoutResult Execute(GameClient session, IIncomingPacket packet, ICameraService camera, Func<CameraCheckoutMedia, CameraCheckoutResult> operation)
    {
        if (!session.IsAuthenticated || session.GetHabbo()?.CurrentRoom == null || !TryReadMediaId(packet, out var id))
            return new(false, "unavailable");
        return camera.Checkout(session, id, operation);
    }

    internal static bool TryReadMediaId(IIncomingPacket packet, out Guid id)
    {
        id = Guid.Empty;
        var bytes = packet.Buffer;
        if (bytes.Length < 2) return false;
        int count = (bytes.Span[0] << 8) | bytes.Span[1];
        if (bytes.Length != count + 2) return false;
        return CameraMediaPath.TryReadId(packet.ReadString(), out id);
    }
}
