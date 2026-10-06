using Dapper;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Achievements;

namespace Plus.HabboHotel.Users.UserData;

public class UserDataFactory : IUserDataFactory
{
    private readonly BadgeManager _badgeManager;
    private readonly IDatabase _database;
    private readonly IEnumerable<IUserDataLoadingTask> _userDataLoadingTasks;
    private readonly IRoomVisitRecorder _roomVisits;
    private readonly IUserPersistenceService _persistence;
    private readonly IUserComponentLoader _components;
    private readonly Clothing.IClothingStore _clothingStore;
    private readonly TimeProvider _time;
    private readonly IAchievementManager _achievements;

    public UserDataFactory(BadgeManager badgeManager, IDatabase database, IEnumerable<IUserDataLoadingTask> userDataLoadingTasks, IUserPersistenceService persistence, IUserComponentLoader components, Clothing.IClothingStore clothingStore, IRoomVisitRecorder roomVisits, TimeProvider time, IAchievementManager achievements)
    {
        _badgeManager = badgeManager;
        _database = database;
        _userDataLoadingTasks = userDataLoadingTasks;
        _persistence = persistence;
        _components = components;
        _clothingStore = clothingStore;
        _roomVisits = roomVisits;
        _time = time;
        _achievements = achievements;
    }

    public async Task<Habbo?> Create(int userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var habbo = await LoadHabboInfo(userId);

        if (habbo == null)
        {
            return null;
        }

        habbo.Persistence = _persistence;
        habbo.SessionStartedAt = _time.GetUtcNow();
        var components = _components.Load(userId);
        habbo.Clothing = new(components.Clothing, habbo, _clothingStore);
        habbo.Effects = new(components.Effects, habbo, _time);

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

        return await connection.ExecuteScalarAsync<string>("SELECT username FROM users WHERE id = @userId", new
        {
            userId
        });
    }

    public async Task<bool> HabboExists(int userId)
    {
        using var connection = _database.Connection();

        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(0) FROM `users` WHERE `id` = @userId LIMIT 1", new
        {
            userId
        }) != 0;
    }

    public async Task<bool> HabboExists(string username)
    {
        using var connection = _database.Connection();

        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(0) FROM `users` WHERE `username` = @username LIMIT 1", new
        {
            username
        }) != 0;
    }

    public async Task<Habbo?> GetUserDataByIdAsync(int userId) => await LoadHabboInfo(userId);

    private async Task<Habbo?> LoadHabboInfo(int userId)
    {
        using var connection = _database.Connection();
        var users = await connection.QueryAsync<Habbo, string, Habbo>(
            "SELECT u.`id`, u.`username`, u.`motto`, u.`look`, u.`gender`, u.`last_online` AS LastOnlineAt, u.`credits`, u.`activity_points` as Duckets, us.`home_room`, us.`block_newfriends` as AllowFriendRequests, us.`hide_online` as AppearOffline, us.`hide_inroom` as AllowPublicRoomStatus, u.`vip`, u.`account_created` AS AccountCreatedAt, u.`vip_points` as Diamonds, us.`chat_preference`, us.`focus_preference`, us.`pets_muted` as AllowPetSpeech, us.`bots_muted` as AllowBotSpeech, us.`advertising_report_blocked`, u.`last_change` as LastNameChangedAt, u.`gotw_points`, us.`ignore_invites` as AllowMessengerInvites, u.`time_muted`, us.`allow_gifts`, us.`friend_bar_state`, us.`disable_forced_effects`, us.`allow_mimic`, u.`bubble_id` as CustomBubbleId, s.`AchievementScore` as AchievementPoints, s.`groupid` as FavouriteGroupId, i.`trading_locked` AS TradingLockExpiresAt, us.`volume` AS Volume " +
            "FROM `users` u " +
            "INNER JOIN `users_settings` us ON us.user_id = u.id " +
            "LEFT JOIN `user_statistics` s ON u.id = s.id " +
            "LEFT JOIN `user_info` i ON u.id = i.user_id " +
            "WHERE u.`id` = @userId LIMIT 1",
            (user, volume) =>
            {
                user.ClientVolume = ParseVolumes(volume);

                return user;
            },
            new
            {
                userId
            }, splitOn: "Volume");
        var habbo = users.SingleOrDefault();
        habbo?.SetRoomVisitRecorder(_roomVisits, _achievements);

        return habbo;
    }

    internal static List<int> ParseVolumes(string? volume) =>
        (volume ?? string.Empty).Split(',').Take(3)
            .Select(value => int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0 and <= 100 ? parsed : 100)
            .Concat(Enumerable.Repeat(100, 3)).Take(3).ToList();

    public async Task<List<Badge>> GetEquippedBadgesForUserAsync(int userId)
    {
        var allBadges = await _badgeManager.LoadBadgesForHabbo(userId);

        return allBadges.Where(b => b.Slot > 0).ToList();
    }
}
