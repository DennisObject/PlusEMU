using Plus.HabboHotel.Users.Badges;

namespace Plus.HabboHotel.Users.UserData;

public interface IUserDataFactory
{
    Task<Habbo?> Create(int userId, CancellationToken cancellationToken = default);
    Task<string> GetUsernameForHabboById(int userId);
    Task<bool> HabboExists(int userId);
    Task<bool> HabboExists(string username);
    Task<Habbo?> GetUserDataByIdAsync(int userId);
    Task<List<Badge>> GetEquippedBadgesForUserAsync(int userId);
}
