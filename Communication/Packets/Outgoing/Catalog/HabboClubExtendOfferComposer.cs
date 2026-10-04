using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class HabboClubExtendOfferComposer(ClubOffer offer, DateTime membershipEnd, DateTime now) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabboClubExtendOfferComposer;
    public void Compose(IOutgoingPacket packet)
    {
        HabboClubOffersComposer.WriteOffer(packet, offer, membershipEnd, now);
        packet.WriteInteger(offer.Months > 0 ? offer.Credits / offer.Months : offer.Credits);
        packet.WriteInteger(offer.Months > 0 ? offer.Points / offer.Months : offer.Points);
        packet.WriteInteger(offer.PointsType);
        packet.WriteInteger((int)Math.Ceiling((membershipEnd - now).TotalDays));
    }
}
