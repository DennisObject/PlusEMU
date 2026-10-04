using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminMovePageEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminMovePageEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int pageId = packet.ReadInt(), parentId = packet.ReadInt(), index = packet.ReadInt();
        var envelope = CatalogAdminPacketReader.Envelope(packet, CatalogAdminPacketReader.CatalogType(packet.ReadString()));
        CatalogAdminResponder.Send(session, "movePage", envelope, _catalogAdmin.MovePage(session.GetHabbo(), envelope, pageId, parentId, index));
        return Task.CompletedTask;
    }
}
