using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public sealed class GetCatalogIndexEvent(ICatalogManager catalog, ICatalogSnapshotService snapshots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadString();
        session.Send(new CatalogIndexComposer(snapshots.CaptureIndex(session.GetHabbo(), catalog.Pages)));
        session.Send(new CatalogItemDiscountComposer());
        return Task.CompletedTask;
    }
}
