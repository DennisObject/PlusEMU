using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Subscriptions;

public interface IClubMembershipService
{
    int GetExpiry(int userId);

    /// <summary>Charges the offer and extends the membership; returns the new expiry, or null when refused.</summary>
    int? Purchase(Habbo habbo, ClubOffer offer);

    /// <summary>Extends the membership by free days (0 ends it now); returns the new expiry.</summary>
    int? Grant(Habbo actor, int userId, int days);
}
