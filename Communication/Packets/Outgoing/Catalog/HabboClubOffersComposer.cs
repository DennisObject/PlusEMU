using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public sealed class HabboClubOffersComposer(ClubOffersSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabboClubOffersComposer;

    internal static void WriteOffer(IOutgoingPacket packet, ClubOfferWireData offer)
    {
        packet.WriteInteger(offer.Id);
        packet.WriteString(offer.Name);
        packet.WriteBoolean(false);
        packet.WriteInteger(offer.Credits);
        packet.WriteInteger(offer.Points);
        packet.WriteInteger(offer.PointsType);
        packet.WriteBoolean(true);
        packet.WriteInteger(offer.Months);
        packet.WriteInteger(offer.ExtraDays);
        packet.WriteBoolean(offer.Giftable);
        packet.WriteInteger(offer.DaysLeft);
        packet.WriteInteger(offer.EndYear);
        packet.WriteInteger(offer.EndMonth);
        packet.WriteInteger(offer.EndDay);
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(data.Offers.Length);
        foreach (var offer in data.Offers)
            WriteOffer(packet, offer);
        packet.WriteInteger(data.WindowId);
    }
}
