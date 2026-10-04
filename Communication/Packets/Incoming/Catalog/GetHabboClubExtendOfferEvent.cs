using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetHabboClubExtendOfferEvent(ICatalogManager catalog, TimeProvider clock) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!catalog.Pages.Any(page => page.CanOpen(session.GetHabbo()) && page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy")) return Task.CompletedTask;
        var offer = catalog.ClubOffers.Where(offer => offer.Days > 0 && offer.Days <= 36500).OrderBy(offer => offer.Days).FirstOrDefault();
        if (offer != null)
        {
            var now = clock.GetUtcNow();
            var end = DateTimeOffset.FromUnixTimeSeconds(Math.Max(now.ToUnixTimeSeconds(), session.GetHabbo().Access.Membership.ExpiresAt));
            session.Send(new HabboClubExtendOfferComposer(offer, end.UtcDateTime, now.UtcDateTime));
        }
        return Task.CompletedTask;
    }
}