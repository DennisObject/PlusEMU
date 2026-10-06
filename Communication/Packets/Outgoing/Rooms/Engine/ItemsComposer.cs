using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemsComposer(RoomFurnitureSnapshot furniture) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ItemsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        RoomEngineSerializers.WriteOwnerMap(packet, furniture);
        packet.WriteInteger(furniture.Items.Length);

        foreach (var item in furniture.Items)
        {
            RoomEngineSerializers.WriteWallItem(packet, item);
        }
    }
}
