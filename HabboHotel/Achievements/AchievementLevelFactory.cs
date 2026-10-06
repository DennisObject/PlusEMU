using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Achievements;

public class AchievementLevelFactory : IAchievementLevelFactory
{
    private readonly IDatabase _database;

    public AchievementLevelFactory(IDatabase database) => _database = database;

    public async Task<Dictionary<string, Achievement>> GetAchievementLevels()
    {
        var achievements = new Dictionary<string, Achievement>();

        using var connection = _database.Connection();
        var table = await connection.QueryAsync<Achievement>("SELECT `id`,`category`,`group_name`,`level`,`reward_pixels`,`reward_points`,`progress_needed`,`game_id` FROM `achievements`");

        foreach (Achievement row in table) {
            var level = new AchievementLevel(row.Level, row.RewardPixels, row.RewardPoints, row.ProgressNeeded);

            if (achievements.ContainsKey(row.GroupName!)) {
                achievements[row.GroupName!].AddLevel(level);
            }
            else {
                row.AddLevel(level);
                achievements.Add(row.GroupName!, row);
            }
        }

        return achievements;
    }
}
