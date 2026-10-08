using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Camera;

public sealed class CameraStorageUrlComposer(string payload, byte[]? png = null) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CameraStorageUrlComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(payload);

        // The stored photo follows the reply, whose "inline" names its length, so the client needs no second request.
        // Older clients read only the reply.
        if (png is not { Length: > 0 }) {
            return;
        }

        packet.WriteInt(png.Length);

        foreach (var value in png) {
            packet.WriteByte(value);
        }
    }
}
