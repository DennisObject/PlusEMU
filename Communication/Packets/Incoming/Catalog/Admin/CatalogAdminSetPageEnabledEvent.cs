using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminSetPageEnabledEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminSetPageEnabledEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (pageId, enabled, envelope) = CatalogAdminPacketReader.PageFlag(packet);
        CatalogAdminResponder.Send(session, "toggleEnabled", envelope, _catalogAdmin.SetPageEnabled(session.GetHabbo(), envelope, pageId, enabled));

        return Task.CompletedTask;
    }
}
