using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Camera;

public sealed class InitCameraComposer(int credits, int duckets, int publishDuckets) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.InitCameraComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(credits);
        packet.WriteInteger(duckets);
        packet.WriteInteger(publishDuckets);
    }
}
