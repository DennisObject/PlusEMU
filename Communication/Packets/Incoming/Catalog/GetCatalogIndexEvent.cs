using Plus.Communication.Packets.Outgoing.BuildersClub;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class GetCatalogIndexEvent : IPacketEvent
{
    private readonly ICatalogManager _catalogManager;
    private readonly ICatalogSnapshotService _snapshots;

    public GetCatalogIndexEvent(ICatalogManager catalogManager, ICatalogSnapshotService snapshots)
    {
        _catalogManager = catalogManager;
        _snapshots = snapshots;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var mode = packet.ReadString();
        session.Send(new CatalogIndexComposer(_snapshots.CaptureIndex(session.GetHabbo(), _catalogManager.Pages, CatalogModes.FromClient(mode))));
        session.Send(new CatalogItemDiscountComposer());
        session.Send(new BcBorrowedItemsComposer());
        return Task.CompletedTask;
    }
}
