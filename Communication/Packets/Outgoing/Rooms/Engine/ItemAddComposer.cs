using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemAddComposer(RoomItemSnapshot item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ItemAddComposer;
    public void Compose(IOutgoingPacket packet)
    {
        RoomEngineSerializers.WriteWallItem(packet, item);
        packet.WriteString(item.Username);
    }
}
