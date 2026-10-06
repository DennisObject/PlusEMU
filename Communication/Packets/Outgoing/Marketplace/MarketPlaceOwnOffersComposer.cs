using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Catalog.Marketplace;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

public class MarketPlaceOwnOffersComposer : IServerPacket
{
    private readonly MarketplaceOwnOffers _data;
    public uint MessageId => ServerPacketHeader.MarketPlaceOwnOffersComposer;

    public MarketPlaceOwnOffersComposer(MarketplaceOwnOffers data) =>
        _data = data with { Offers = data.Offers.ToArray() };

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_data.AccumulatedAmount);
        packet.WriteInteger(_data.Offers.Count);
        foreach (var offer in _data.Offers)
        {
                packet.WriteInteger(offer.OfferId);
                packet.WriteInteger(offer.State);
                packet.WriteInteger(1);
                packet.WriteInteger(offer.SpriteId);
                packet.WriteInteger(256);
                packet.WriteString("");
                packet.WriteInteger(offer.LimitedNumber);
                packet.WriteInteger(offer.LimitedStack);
                packet.WriteInteger(offer.TotalPrice);
                packet.WriteInteger(offer.MinutesRemaining);
                packet.WriteInteger(offer.SpriteId);
        }
    }
}
