using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminSetPageVisibleEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminSetPageVisibleEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (pageId, visible, envelope) = CatalogAdminPacketReader.PageFlag(packet);
        CatalogAdminResponder.Send(session, "toggleVisible", envelope, _catalogAdmin.SetPageVisible(session.GetHabbo(), envelope, pageId, visible));

        return Task.CompletedTask;
    }
}
