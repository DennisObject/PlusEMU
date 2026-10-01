using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users;

public class HabboStatsService : IHabboStatsService
{
    private readonly IDatabase _database;

    public HabboStatsService(IDatabase database) => _database = database;

    public async Task<HabboStats> LoadHabboStats(int userId)
    {
        using var connection = _database.Connection();

        var statRow = await connection.QueryFirstOrDefaultAsync<HabboStatsMaterializer.Row>(
            @"SELECT RoomVisits, OnlineTime, Respect, RespectGiven, GiftsGiven, GiftsReceived, 
              DailyRespectPoints, DailyPetRespectPoints, `AchievementScore` AS AchievementPoints, 
              quest_id AS QuestId, quest_progress AS QuestProgress, groupid AS FavouriteGroupId, 
              respectsTimestamp AS RespectsTimestamp, forum_posts AS ForumPosts 
              FROM `user_statistics` WHERE `id` = @id LIMIT 1",
            new { id = userId });

        if (statRow != null) return HabboStatsMaterializer.ToHabboStats(statRow);
        await connection.ExecuteAsync(
            "INSERT INTO `user_statistics` (`id`) VALUES (@id) ON DUPLICATE KEY UPDATE `id` = VALUES(`id`)",
            new { id = userId });

        return new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);

    }

    public async Task UpdateDailyRespectsAndTimestamp(int userId, int dailyRespects, string respectsTimestamp)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync(
            "UPDATE `user_statistics` SET `DailyRespectPoints` = @dailyRespects, `DailyPetRespectPoints` = @dailyRespects, `RespectsTimestamp` = @respectsTimestamp WHERE `id` = @userId",
            new { dailyRespects, respectsTimestamp, userId });
    }
}