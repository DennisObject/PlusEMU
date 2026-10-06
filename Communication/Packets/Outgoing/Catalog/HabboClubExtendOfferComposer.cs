using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public sealed class HabboClubExtendOfferComposer(ClubExtensionSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabboClubExtendOfferComposer;

    public void Compose(IOutgoingPacket packet)
    {
        HabboClubOffersComposer.WriteOffer(packet, data.Offer);
        packet.WriteInteger(data.MonthlyCredits);
        packet.WriteInteger(data.MonthlyPoints);
        packet.WriteInteger(data.Offer.PointsType);
        packet.WriteInteger(data.MembershipDaysLeft);
    }
}
