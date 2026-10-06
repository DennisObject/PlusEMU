namespace Plus.HabboHotel.Users;

public interface IHabboStatsService
{
    Task<HabboStats> LoadHabboStats(int userId);
    Task UpdateDailyRespectsAndTimestamp(int userId, int dailyRespects, string respectsTimestamp);
}
