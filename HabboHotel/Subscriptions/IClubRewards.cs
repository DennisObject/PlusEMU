using System.Data;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Subscriptions;

public sealed record ClubGift(CatalogOffer Offer, int DaysRequired);
public sealed record ClubGiftInfo(int DaysUntilNextGift, int Available, long PastDays, IReadOnlyList<ClubGift> Gifts);
public sealed record ClubGiftClaim(ClubGift Gift, IReadOnlyList<InventoryItem> Items);
public sealed record ClubKickback(int Streak, string FirstDate, double Percentage, int Missed, int Rewarded,
    int Spent, int StreakBonus, int SpendingBonus, int MinutesUntilPayday);
public interface IClubRewards
{
    ClubGiftInfo Gifts(Habbo habbo);
    ClubGiftClaim? Claim(Habbo habbo, string productCode);
    ClubKickback Kickback(Habbo habbo);
    bool Charge(Habbo habbo, int credits, int points = 0, int pointsType = ActivityPointType.Duckets, Func<IDbConnection, IDbTransaction, bool>? deliver = null, bool kickbackEligible = true);
    void RunPaydays();
}
