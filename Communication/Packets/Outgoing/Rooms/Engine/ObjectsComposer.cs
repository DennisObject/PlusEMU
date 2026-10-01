using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ObjectsComposer : IServerPacket
{
    private readonly Item[] _objects;
    private readonly Room _room;
    public uint MessageId => ServerPacketHeader.ObjectsComposer;

    public ObjectsComposer(Item[] objects, Room room)
    {
        _objects = objects;
        _room = room;
    }

    public void Compose(IOutgoingPacket packet)
    {
        RoomEngineSerializers.WriteOwnerMap(packet, _objects, _room.OwnerId, _room.OwnerName);
        packet.Serialize(_objects);
    }
}
