using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminSavePageImagesEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminSavePageImagesEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int pageId = packet.ReadInt();
        string headerImage = packet.ReadString(), teaserImage = packet.ReadString();
        var envelope = CatalogAdminPacketReader.Envelope(packet, CatalogAdminPacketReader.CatalogType(packet.ReadString()));
        CatalogAdminResponder.Send(session, "savePageImages", envelope, _catalogAdmin.SavePageImages(session.GetHabbo(), envelope, pageId, headerImage, teaserImage));
        return Task.CompletedTask;
    }
}
