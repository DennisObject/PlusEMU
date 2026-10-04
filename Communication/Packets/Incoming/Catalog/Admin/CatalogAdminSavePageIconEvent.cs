using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminSavePageIconEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminSavePageIconEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int pageId = packet.ReadInt(), iconId = packet.ReadInt();
        var envelope = CatalogAdminPacketReader.Envelope(packet, CatalogAdminPacketReader.CatalogType(packet.ReadString()));
        CatalogAdminResponder.Send(session, "savePageIcon", envelope, _catalogAdmin.SavePageIcon(session.GetHabbo(), envelope, pageId, iconId));
        return Task.CompletedTask;
    }
}
