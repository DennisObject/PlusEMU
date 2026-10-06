namespace Plus.HabboHotel.Achievements;

public interface IAchievementLevelFactory
{
    Task<Dictionary<string, Achievement>> GetAchievementLevels();
}
