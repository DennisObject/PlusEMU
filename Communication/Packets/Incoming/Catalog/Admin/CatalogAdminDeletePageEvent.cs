using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogAdminDeletePageEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminDeletePageEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (pageId, envelope) = CatalogAdminPacketReader.Target(packet);
        CatalogAdminResponder.Send(session, "deletePage", envelope, _catalogAdmin.DeletePage(session.GetHabbo(), envelope, pageId));
        return Task.CompletedTask;
    }
}
