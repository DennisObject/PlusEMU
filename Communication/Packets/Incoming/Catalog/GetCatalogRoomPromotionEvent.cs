using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetCatalogRoomPromotionEvent(IRoomPromotionService promotions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        promotions.ShowCatalogRooms(session);

        return Task.CompletedTask;
    }
}
