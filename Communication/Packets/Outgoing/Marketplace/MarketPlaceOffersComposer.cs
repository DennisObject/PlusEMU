using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

public class MarketPlaceOffersComposer : IServerPacket
{
    private readonly MarketplaceOffersSnapshot _offers;
    public uint MessageId => ServerPacketHeader.MarketPlaceOffersComposer;

    public MarketPlaceOffersComposer(MarketplaceOffersSnapshot offers)
    {
        _offers = offers;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_offers.Offers.Length);
        foreach (var offer in _offers.Offers)
        {
            packet.WriteUInteger(offer.OfferId);
            packet.WriteInteger(1); //State
            packet.WriteInteger(1);
            packet.WriteUInteger(offer.SpriteId);
            packet.WriteInteger(256);
            packet.WriteString("");
            packet.WriteUInteger(offer.LimitedNumber);
            packet.WriteUInteger(offer.LimitedStack);
            packet.WriteInteger(offer.TotalPrice);
            packet.WriteInteger(0);
            packet.WriteInteger(offer.AveragePrice);
            packet.WriteInteger(offer.Count);
        }
        packet.WriteInteger(_offers.Offers.Length); //Item count to show how many were found.
    }
}
