using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class GetCatalogPageEvent : IPacketEvent
{
    private readonly ICatalogManager _catalogManager;

    public GetCatalogPageEvent(ICatalogManager catalogManager)
    {
        _catalogManager = catalogManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var pageId = packet.ReadInt();
        var offerId = packet.ReadInt();
        packet.ReadString(); // catalog mode; the page answers with its own
        if (!_catalogManager.TryGetPage(pageId, out var page))
            return Task.CompletedTask;
        if (!page.CanOpen(session.GetHabbo()))
            return Task.CompletedTask;
        session.Send(new CatalogPageComposer(page, page.CatalogMode, page.Offers.ContainsKey(offerId) ? offerId : -1));
        return Task.CompletedTask;
    }
}