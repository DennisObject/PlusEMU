using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ObjectUpdateComposer(RoomItemSnapshot item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ObjectUpdateComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.Serialize(item);
    }
}
