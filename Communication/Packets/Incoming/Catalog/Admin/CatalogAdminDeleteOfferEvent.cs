using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogAdminDeleteOfferEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminDeleteOfferEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (offerId, envelope) = CatalogAdminPacketReader.Target(packet);
        CatalogAdminResponder.Send(session, "deleteOffer", envelope, _catalogAdmin.DeleteOffer(session.GetHabbo(), envelope, offerId));
        return Task.CompletedTask;
    }
}
