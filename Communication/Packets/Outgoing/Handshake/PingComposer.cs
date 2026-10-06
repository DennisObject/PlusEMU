using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class PingComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PingComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
