using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class GetCatalogPageEvent : IPacketEvent
{
    private readonly ICatalogManager _catalogManager;
    private readonly ICatalogAdminService _catalogAdmin;
    private readonly ICatalogSnapshotService _snapshots;

    public GetCatalogPageEvent(ICatalogManager catalogManager, ICatalogAdminService catalogAdmin, ICatalogSnapshotService snapshots)
    {
        _catalogManager = catalogManager;
        _catalogAdmin = catalogAdmin;
        _snapshots = snapshots;
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
        _catalogAdmin.RecordViewedPage(session.GetHabbo(), page.Id);
        session.Send(new CatalogPageComposer(_snapshots.CapturePage(page, page.Offers.ContainsKey(offerId) ? offerId : -1)));
        return Task.CompletedTask;
    }
}
