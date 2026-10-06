using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class PurchaseRoomAdEvent(IRoomPromotionService promotions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); // Page ID.
        packet.ReadInt(); // Item ID.
        var roomId = packet.ReadUInt();
        var name = packet.ReadString();
        packet.ReadBool(); // Unused client flag.
        var description = packet.ReadString();
        var categoryId = packet.ReadInt();
        return promotions.Purchase(session, new(roomId, name, description, categoryId));
    }
}
