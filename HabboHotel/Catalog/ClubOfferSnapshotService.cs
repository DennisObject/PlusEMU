using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Catalog;

public sealed record ClubOfferWireData(int Id, string Name, int Credits, int Points, int PointsType,
    int Months, int ExtraDays, bool Giftable, int DaysLeft, int EndYear, int EndMonth, int EndDay);
public sealed record ClubOffersSnapshot(ImmutableArray<ClubOfferWireData> Offers, int WindowId);
public sealed record ClubExtensionSnapshot(ClubOfferWireData Offer, int MonthlyCredits, int MonthlyPoints,
    int MembershipDaysLeft);

public static class ClubOfferSnapshotFactory
{
    public static ClubOffersSnapshot Capture(IEnumerable<ClubOffer> offers, int windowId, DateTimeOffset membershipEnd, DateTimeOffset now)
    {
        var end = membershipEnd > now ? membershipEnd : now;

        return new(offers.Select(offer => Capture(offer, end, now)).ToImmutableArray(), windowId);
    }

    public static ClubOfferWireData Capture(ClubOffer offer, DateTimeOffset membershipEnd, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        membershipEnd = membershipEnd.ToUniversalTime();
        var endsAt = membershipEnd.AddDays(offer.Days);

        return new(offer.Id, offer.Name, offer.Credits, offer.Points, offer.PointsType, offer.Months,
            offer.ExtraDays, offer.Giftable, (int)Math.Ceiling((endsAt - now).TotalDays),
            endsAt.Year, endsAt.Month, endsAt.Day);
    }
}

public interface IClubOfferSnapshotService
{
    void ShowOffers(GameClient session, int windowId);
    void ShowExtension(GameClient session);
}

public sealed class ClubOfferSnapshotService(ICatalogManager catalog, TimeProvider clock) : IClubOfferSnapshotService
{
    public void ShowOffers(GameClient session, int windowId)
    {
        var now = clock.GetUtcNow();
        var end = MembershipEnd(session, now);
        session.Send(new HabboClubOffersComposer(ClubOfferSnapshotFactory.Capture(catalog.ClubOffers, windowId, end, now)));
    }

    public void ShowExtension(GameClient session)
    {
        if (!catalog.Pages.Any(page => page.CanOpen(session.GetHabbo()) && page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy"))
        {
            return;
        }

        var offer = catalog.ClubOffers.Where(offer => offer.Days > 0 && offer.Days <= 36500).OrderBy(offer => offer.Days).FirstOrDefault();

        if (offer == null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var end = MembershipEnd(session, now);
        var snapshot = new ClubExtensionSnapshot(ClubOfferSnapshotFactory.Capture(offer, end, now),
            offer.Months > 0 ? offer.Credits / offer.Months : offer.Credits,
            offer.Months > 0 ? offer.Points / offer.Months : offer.Points,
            (int)Math.Ceiling((end - now).TotalDays));
        session.Send(new HabboClubExtendOfferComposer(snapshot));
    }

    private static DateTimeOffset MembershipEnd(GameClient session, DateTimeOffset now)
    {
        var expiry = session.GetHabbo().Access.Membership.ExpiresAt;

        return expiry is { } end && end > now ? end : now;
    }
}
