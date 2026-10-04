using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogAdminMoveOfferEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminMoveOfferEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int offerId = packet.ReadInt(), orderNumber = packet.ReadInt();
        var envelope = CatalogAdminPacketReader.Envelope(packet, CatalogAdminPacketReader.CatalogType(packet.ReadString()));
        CatalogAdminResponder.Send(session, "moveOffer", envelope, _catalogAdmin.MoveOffer(session.GetHabbo(), envelope, offerId, orderNumber));
        return Task.CompletedTask;
    }
}
