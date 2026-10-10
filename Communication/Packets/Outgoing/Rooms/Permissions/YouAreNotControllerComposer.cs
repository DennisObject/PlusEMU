using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Permissions;

public class YouAreNotControllerComposer(uint roomId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.YouAreNotControllerComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteUInteger(roomId);
}
