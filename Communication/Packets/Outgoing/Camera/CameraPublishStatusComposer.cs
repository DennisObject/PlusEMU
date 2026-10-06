using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Camera;

public sealed class CameraPublishStatusComposer(bool ok, int secondsToWait, string id = "") : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CameraPublishStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(ok);
        packet.WriteInteger(secondsToWait);

        if (ok) {
            packet.WriteString(id);
        }
    }
}
