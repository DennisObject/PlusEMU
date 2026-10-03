using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class HabboClubOffersComposer : IServerPacket
{
    private readonly ICollection<ClubOffer> _offers;
    private readonly int _windowId;
    private readonly DateTime _membershipEnd;
    public uint MessageId => ServerPacketHeader.HabboClubOffersComposer;

    // membershipEnd is when the buyer's current membership runs out, or now if they have none.
    public HabboClubOffersComposer(ICollection<ClubOffer> offers, int windowId, DateTime membershipEnd)
    {
        _offers = offers;
        _windowId = windowId;
        _membershipEnd = membershipEnd;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_offers.Count);
        foreach (var offer in _offers)
        {
            var endsAt = _membershipEnd.AddDays(offer.Days);
            packet.WriteInteger(offer.Id);
            packet.WriteString(offer.Name);
            packet.WriteBoolean(false); // unused
            packet.WriteInteger(offer.Credits);
            packet.WriteInteger(offer.Points);
            packet.WriteInteger(offer.PointsType);
            packet.WriteBoolean(offer.Vip);
            packet.WriteInteger(offer.Months);
            packet.WriteInteger(offer.ExtraDays);
            packet.WriteBoolean(offer.Giftable);
            packet.WriteInteger((int)Math.Ceiling((endsAt - DateTime.UtcNow).TotalDays)); // days left after purchase
            packet.WriteInteger(endsAt.Year);
            packet.WriteInteger(endsAt.Month);
            packet.WriteInteger(endsAt.Day);
        }
        packet.WriteInteger(_windowId);
    }
}
