using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Permissions;

public class YouAreControllerComposer(uint roomId, int setting) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.YouAreControllerComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(roomId);
        packet.WriteInteger(setting);
    }
}
