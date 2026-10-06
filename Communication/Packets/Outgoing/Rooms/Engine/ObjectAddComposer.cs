using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ObjectAddComposer(RoomItemSnapshot item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ObjectAddComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.Serialize(item);
        packet.WriteString(item.Username);
    }
}
