using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminCreateOfferEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminCreateOfferEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (offer, envelope) = CatalogAdminPacketReader.Offer(packet, hasOfferId: false);
        CatalogAdminResponder.Send(session, "createOffer", envelope, _catalogAdmin.CreateOffer(session.GetHabbo(), envelope, offer));

        return Task.CompletedTask;
    }
}
