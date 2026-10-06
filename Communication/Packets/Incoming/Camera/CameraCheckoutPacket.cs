using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

internal static class CameraCheckoutPacket
{
    internal static Guid? ReadMediaId(IIncomingPacket packet) =>
        TryReadMediaId(packet, out var id) ? id : null;

    internal static bool TryReadMediaId(IIncomingPacket packet, out Guid id)
    {
        id = Guid.Empty;
        var bytes = packet.Buffer;

        if (bytes.Length < 2) {
            return false;
        }

        int count = (bytes.Span[0] << 8) | bytes.Span[1];

        if (bytes.Length != count + 2) {
            return false;
        }

        return CameraMediaPath.TryReadId(packet.ReadString(), out id);
    }
}
