using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Session;

public class OpenConnectionComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.OpenConnectionComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
