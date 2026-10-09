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

        packet.WriteBoolean(true); // VIP eligibility flag.
        packet.WriteInteger(_usersRooms.Length); // Available room count.

        foreach (var room in _usersRooms) {
            packet.WriteUInteger(room.Id);
            packet.WriteString(room.Name);
            packet.WriteBoolean(true);
        }
    }
}
