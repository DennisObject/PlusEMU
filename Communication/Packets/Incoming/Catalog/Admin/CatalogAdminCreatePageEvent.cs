using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogAdminCreatePageEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminCreatePageEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (page, envelope) = CatalogAdminPacketReader.CreatePage(packet);
        CatalogAdminResponder.Send(session, "createPage", envelope, _catalogAdmin.CreatePage(session.GetHabbo(), envelope, page));
        return Task.CompletedTask;
    }
}
