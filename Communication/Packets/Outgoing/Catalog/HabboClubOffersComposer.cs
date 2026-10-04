using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class HabboClubOffersComposer : IServerPacket
{
    private readonly ICollection<ClubOffer> _offers;
    private readonly int _windowId;
    private readonly DateTime _membershipEnd;
    private readonly DateTime _now;
    public uint MessageId => ServerPacketHeader.HabboClubOffersComposer;

    // membershipEnd is when the buyer's current membership runs out, or now if they have none.
    public HabboClubOffersComposer(ICollection<ClubOffer> offers, int windowId, DateTime membershipEnd, DateTime? now = null)
    {
        _offers = offers;
        _windowId = windowId;
        _now = now ?? DateTime.UtcNow;
        _membershipEnd = membershipEnd > _now ? membershipEnd : _now;
    }

    internal static void WriteOffer(IOutgoingPacket packet, ClubOffer offer, DateTime membershipEnd, DateTime now)
    {
        var endsAt = membershipEnd.AddDays(offer.Days);
        packet.WriteInteger(offer.Id);
        packet.WriteString(offer.Name);
        packet.WriteBoolean(false); // unused
        packet.WriteInteger(offer.Credits);
        packet.WriteInteger(offer.Points);
        packet.WriteInteger(offer.PointsType);
        packet.WriteBoolean(true); // vip_buy displays this single HC list only when the wire flag is true.
        packet.WriteInteger(offer.Months);
        packet.WriteInteger(offer.ExtraDays);
        packet.WriteBoolean(offer.Giftable);
        packet.WriteInteger((int)Math.Ceiling((endsAt - now).TotalDays)); // days left after purchase
        packet.WriteInteger(endsAt.Year);
        packet.WriteInteger(endsAt.Month);
        packet.WriteInteger(endsAt.Day);
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_offers.Count);
        foreach (var offer in _offers)
        {
            WriteOffer(packet, offer, _membershipEnd, _now);
        }
        packet.WriteInteger(_windowId);
    }
}
