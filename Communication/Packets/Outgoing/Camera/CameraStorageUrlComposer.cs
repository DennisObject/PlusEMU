using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Camera;

public sealed class CameraStorageUrlComposer(string payload) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CameraStorageUrlComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(payload);
    }
}
