using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class ClubGiftsComposer(ClubGiftsSnapshot gifts) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ClubGiftsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(gifts.DaysUntilNextGift);
        packet.WriteInteger(gifts.Available);
        packet.WriteInteger(gifts.Offers.Length);
        foreach (var offer in gifts.Offers)
            CatalogOfferWriter.Write(packet, offer);
        packet.WriteInteger(gifts.Gifts.Length);
        foreach (var gift in gifts.Gifts)
        {
            packet.WriteInteger(gift.WireOfferId);
            packet.WriteBoolean(false); // one HC membership
            packet.WriteInteger(gift.DaysRequired);
            packet.WriteBoolean(gift.Unlocked);
        }
    }
}
