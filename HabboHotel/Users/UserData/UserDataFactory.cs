using Dapper;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Users.Badges;

namespace Plus.HabboHotel.Users.UserData;

public class UserDataFactory : IUserDataFactory
{
    private readonly BadgeManager _badgeManager;
    private readonly IDatabase _database;
    private readonly IEnumerable<IUserDataLoadingTask> _userDataLoadingTasks;

    public UserDataFactory(BadgeManager badgeManager, IDatabase database, IEnumerable<IUserDataLoadingTask> userDataLoadingTasks)
    {
        _badgeManager = badgeManager;
        _database = database;
        _userDataLoadingTasks = userDataLoadingTasks;
    }

    public async Task<Habbo?> Create(int userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var habbo = await LoadHabboInfo(userId);

        foreach (var task in _userDataLoadingTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await task.Load(habbo);
        }

        return habbo;
    }

    public async Task<string> GetUsernameForHabboById(int userId)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<string>("SELECT username FROM users WHERE id = @userId", new { userId });
    }

    public async Task<bool> HabboExists(int userId)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(0) FROM `users` WHERE `id` = @userId LIMIT 1", new { userId }) != 0;
    }

    public async Task<bool> HabboExists(string username)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(0) FROM `users` WHERE `username` = @username LIMIT 1", new { username }) != 0;
    }

    public async Task<Habbo?> GetUserDataByIdAsync(int userId) => await LoadHabboInfo(userId);

    private async Task<Habbo> LoadHabboInfo(int userId)
    {
        using var connection = _database.Connection();
        var habbo = await connection.QuerySingleOrDefaultAsync<Habbo>(
            "SELECT u.`id`, u.`username`, u.`rank`, u.`motto`, u.`look`, u.`gender`, u.`last_online`, u.`credits`, u.`activity_points` as Duckets, u.`home_room`, u.`block_newfriends` = true as AllowFriendRequests, u.`hide_online` = true as AppearOffline, u.`hide_inroom` = true as AllowPublicRoomStatus, u.`vip`, u.`account_created`, u.`vip_points` as Diamonds, u.`chat_preference` = true as `chat_preference`, u.`focus_preference` = true as `focus_preference`, u.`pets_muted` = true as AllowPetSpeech, u.`bots_muted` = true as AllowBotSpeech, u.`advertising_report_blocked` = true as advertising_report_blocked, u.`last_change` as LastNameChange, u.`gotw_points`, u.`ignore_invites` = true as AllowMessengerInvites, u.`time_muted`, u.`allow_gifts` = true as `allow_gifts`, u.`friend_bar_state`, u.`disable_forced_effects` = true as `disable_forced_effects`, u.`allow_mimic` = true as `allow_mimic`, u.`rank_vip` as VipRank, u.`is_ambassador`, u.`bubble_id` as CustomBubbleId, s.`AchievementScore` as AchievementPoints, s.`groupid` as FavouriteGroupId " +
            "FROM `users` u " +
            "LEFT JOIN `user_statistics` s ON u.id = s.id " +
            "WHERE u.`id` = @userId LIMIT 1",
            new { userId });
        return habbo;
    }

    public async Task<List<Badge>> GetEquippedBadgesForUserAsync(int userId)
    {
        var allBadges = await _badgeManager.LoadBadgesForHabbo(userId);
        return allBadges.Where(b => b.Slot > 0).ToList();
    }
}
