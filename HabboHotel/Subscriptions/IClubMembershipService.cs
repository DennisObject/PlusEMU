using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Subscriptions;

public interface IClubMembershipService
{
    long GetExpiry(int userId);

    /// <summary>Charges the offer and extends the membership; returns the new expiry, or null when refused.</summary>
    long? Purchase(Habbo habbo, ClubOffer offer, int? recipientId = null);

    /// <summary>Extends the membership by free days (0 ends it now); returns the new expiry.</summary>
    long? Grant(Habbo actor, int userId, int days);
}
