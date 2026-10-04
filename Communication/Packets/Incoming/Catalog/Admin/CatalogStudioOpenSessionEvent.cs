using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogStudioOpenSessionEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogStudioOpenSessionEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        CatalogAdminResponder.Read(session, () => new CatalogStudioSessionComposer(_catalogAdmin.OpenSession(session.GetHabbo())));
        return Task.CompletedTask;
    }
}
