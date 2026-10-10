using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Field order of Volt-Renderer's CatalogAdminOfferDetailsMessageParser.
public sealed class CatalogAdminOfferDetailsComposer : IServerPacket
{
    private readonly CatalogAdminOffer _offer;

    public uint MessageId => ServerPacketHeader.CatalogAdminOfferDetailsComposer;

    public CatalogAdminOfferDetailsComposer(CatalogAdminOffer offer) => _offer = offer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_offer.OfferId);
        packet.WriteInteger(_offer.PageId);
        packet.WriteString(_offer.ItemIds);
        packet.WriteString(_offer.CatalogName);
        packet.WriteInteger(_offer.CostCredits);
        packet.WriteInteger(_offer.CostPoints);
        packet.WriteInteger(_offer.PointsType);
        packet.WriteInteger(_offer.Amount);
        packet.WriteBoolean(_offer.ClubOnly);
        packet.WriteString(_offer.Extradata);
        packet.WriteBoolean(_offer.HaveOffer);
        packet.WriteInteger(_offer.OfferIdClient);
        packet.WriteInteger(_offer.LimitedStack);
        packet.WriteInteger(_offer.LimitedSells);
        packet.WriteInteger(_offer.OrderNumber);
        packet.WriteInteger(_offer.SongId);
        packet.WriteString(_offer.CatalogType);
    }
}
