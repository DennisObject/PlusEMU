using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class GetCatalogRoomPromotionComposer : IServerPacket
{
    private readonly ImmutableArray<PromotionRoomSnapshot> _usersRooms;

    public GetCatalogRoomPromotionComposer(ImmutableArray<PromotionRoomSnapshot> usersRooms)
    {
        _usersRooms = usersRooms;
    }

    public uint MessageId => ServerPacketHeader.PromotableRoomsComposer;

    public void Compose(IOutgoingPacket packet)
    {

        packet.WriteBoolean(true); //wat
        packet.WriteInteger(_usersRooms.Length); //Count of rooms?
        foreach (var room in _usersRooms)
        {
            packet.WriteUInteger(room.Id);
            packet.WriteString(room.Name);
            packet.WriteBoolean(true);
        }
    }
}