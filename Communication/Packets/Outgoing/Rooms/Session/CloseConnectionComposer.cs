using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Session;

public class CloseConnectionComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CloseConnectionComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
