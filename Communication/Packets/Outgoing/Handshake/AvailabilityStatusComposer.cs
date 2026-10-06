using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class AvailabilityStatusComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AvailabilityStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // TODO @80O: Pass variables via constructor.
        packet.WriteBoolean(true);
        packet.WriteBoolean(false);
        packet.WriteBoolean(true);
    }
}
