namespace Plus.HabboHotel.Subscriptions;

public interface IClubMembershipService
{
    int GetExpiry(int userId);
    int Extend(int userId, int days);
}
