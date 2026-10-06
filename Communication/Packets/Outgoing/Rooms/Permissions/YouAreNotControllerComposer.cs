using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Permissions;

public class YouAreNotControllerComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.YouAreNotControllerComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
