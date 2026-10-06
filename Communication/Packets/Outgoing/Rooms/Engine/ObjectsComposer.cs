using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ObjectsComposer(RoomFurnitureSnapshot furniture) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ObjectsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        RoomEngineSerializers.WriteOwnerMap(packet, furniture);
        packet.WriteInteger(furniture.Items.Length);

        foreach (var item in furniture.Items) {
            packet.Serialize(item);
        }
    }
}
