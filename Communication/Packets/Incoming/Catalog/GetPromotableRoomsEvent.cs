using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal sealed class GetPromotableRoomsEvent(ICatalogBrowsingService catalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        catalog.ShowPromotableRooms(session);
        return Task.CompletedTask;
    }
}
