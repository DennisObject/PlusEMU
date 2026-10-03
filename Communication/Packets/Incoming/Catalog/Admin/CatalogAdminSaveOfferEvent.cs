using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogAdminSaveOfferEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminSaveOfferEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (offer, envelope) = CatalogAdminPacketReader.Offer(packet, hasOfferId: true);
        CatalogAdminResponder.Send(session, "saveOffer", envelope, _catalogAdmin.SaveOffer(session.GetHabbo(), envelope, offer));
        return Task.CompletedTask;
    }
}
