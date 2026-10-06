using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public sealed class GetCatalogPageEvent(ICatalogBrowsingService catalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var pageId = packet.ReadInt();
        var offerId = packet.ReadInt();
        var mode = packet.ReadString();
        catalog.ShowPage(session, new(pageId, offerId, mode));

        return Task.CompletedTask;
    }
}
