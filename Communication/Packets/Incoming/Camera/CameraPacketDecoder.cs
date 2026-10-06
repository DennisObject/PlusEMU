using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

internal static class CameraPacketDecoder
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static CameraRequestPayload Decode(IIncomingPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var buffer = packet.Buffer;

        if (buffer.Length >= PngMagic.Length && buffer.Span.StartsWith(PngMagic))
        {
            return Invalid(CameraRejectReason.Pixels);
        }

        if (buffer.Length < 2)
        {
            return Invalid(CameraRejectReason.Schema);
        }

        var declared = (buffer.Span[0] << 8) | buffer.Span[1];

        if (declared > CameraRequestParser.MaxJsonBytes)
        {
            return Invalid(CameraRejectReason.Oversized);
        }

        if (buffer.Length < declared + 2)
        {
            return Invalid(CameraRejectReason.Schema);
        }

        if (buffer.Length > declared + 2)
        {
            return Invalid(CameraRejectReason.Trailing);
        }

        try
        {
            return new(packet.ReadString());
        }
        catch (Exception)
        {
            return Invalid(CameraRejectReason.Schema);
        }
    }

    private static CameraRequestPayload Invalid(CameraRejectReason error) => new(null) { FrameError = error };
}
