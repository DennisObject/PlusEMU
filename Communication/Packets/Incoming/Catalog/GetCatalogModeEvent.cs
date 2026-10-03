using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetCatalogModeEvent : IPacketEvent
{
    private readonly ICatalogManager _catalog;

    public GetCatalogModeEvent(ICatalogManager catalog)
    {
        _catalog = catalog;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var mode = packet.ReadString();
        session.Send(new CatalogIndexComposer(session, _catalog.Pages, CatalogModes.FromClient(mode)));
        return Task.CompletedTask;
    }
}