using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemUpdateComposer(RoomItemSnapshot item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ItemUpdateComposer;
    public void Compose(IOutgoingPacket packet)
    {
        RoomEngineSerializers.WriteWallItem(packet, item);
    }
}
