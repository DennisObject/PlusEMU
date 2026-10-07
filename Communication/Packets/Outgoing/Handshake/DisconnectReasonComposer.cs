using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class DisconnectReasonComposer(DisconnectReason reason) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.DisconnectReasonComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)reason);
    }
}
