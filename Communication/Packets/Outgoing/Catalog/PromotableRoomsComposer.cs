using System.Collections.Immutable;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public sealed class PromotableRoomsComposer(ImmutableArray<PromotableRoomSnapshot> rooms) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PromotableRoomsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true);
        packet.WriteInteger(rooms.Length);
        foreach (var room in rooms)
        {
            packet.WriteUInteger(room.Id);
            packet.WriteString(room.Name);
            packet.WriteBoolean(false);
        }
    }
}
