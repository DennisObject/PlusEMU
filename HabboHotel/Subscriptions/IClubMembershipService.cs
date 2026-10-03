using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Subscriptions;

public interface IClubMembershipService
{
    int GetExpiry(int userId);

    /// <summary>Charges the offer and extends the membership; returns the new expiry, or null when refused.</summary>
    int? Purchase(Habbo habbo, ClubOffer offer);
}
