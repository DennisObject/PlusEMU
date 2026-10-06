using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Permissions;

public class YouAreOwnerComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.YouAreOwnerComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
