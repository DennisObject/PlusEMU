using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;

namespace Plus.HabboHotel.Badges;

public interface IBadgeManager
{
    Task Init();
    Task GiveBadge(Habbo habbo, string code);
    Task RemoveBadge(Habbo habbo, string badge);
    Task<List<Badge>> LoadBadgesForHabbo(int userId);
    IReadOnlyDictionary<string, BadgeDefinition> Badges
    {
        get;
    }
}
