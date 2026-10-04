using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogAdminSavePageEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminSavePageEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (page, envelope) = CatalogAdminPacketReader.SavePage(packet);
        CatalogAdminResponder.Send(session, "savePage", envelope, _catalogAdmin.SavePage(session.GetHabbo(), envelope, page));
        return Task.CompletedTask;
    }
}
