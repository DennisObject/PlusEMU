using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Camera;

public sealed class ThumbnailStatusComposer(bool ok, bool renderLimitHit = false) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ThumbnailStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(ok);
        packet.WriteBoolean(renderLimitHit);
    }
}
