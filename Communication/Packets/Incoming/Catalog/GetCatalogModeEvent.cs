using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetCatalogModeEvent : IPacketEvent
{
    private readonly ICatalogManager _catalog;
    private readonly ICatalogSnapshotService _snapshots;

    public GetCatalogModeEvent(ICatalogManager catalog, ICatalogSnapshotService snapshots)
    {
        _catalog = catalog;
        _snapshots = snapshots;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadString();
        session.Send(new CatalogIndexComposer(_snapshots.CaptureIndex(session.GetHabbo(), _catalog.Pages)));
        return Task.CompletedTask;
    }
}
