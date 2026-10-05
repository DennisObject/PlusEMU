using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Subscriptions;

public interface IClubMembershipService
{
    DateTimeOffset? GetExpiry(int userId);

    /// <summary>Charges the offer and extends the membership; returns the new expiry in UTC, or null when refused.</summary>
    DateTimeOffset? Purchase(Habbo habbo, ClubOffer offer, int? recipientId = null);

    /// <summary>Extends the membership by free days (0 ends it now); returns the new expiry.</summary>
    DateTimeOffset? Grant(Habbo actor, int userId, int days);
}
