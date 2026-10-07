using Dapper;
using Plus.Database;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Achievements;

internal sealed class LoadUserAchievementsTask(IDatabase database) : IUserDataLoadingTask
{
    public async Task Load(Habbo habbo)
    {
        using var connection = database.Connection();
        var rows = await connection.QueryAsync<AchievementRow>(
            "SELECT `group` AS AchievementGroup,level,progress FROM user_achievements WHERE userid=@userId",
            new { userId = habbo.Id });

        foreach (var row in rows) {
            habbo.Achievements[row.AchievementGroup] = new(row.AchievementGroup, row.Level, row.Progress);
        }
    }

    private sealed class AchievementRow
    {
        public string AchievementGroup { get; set; } = string.Empty;
        public int Level { get; set; }
        public int Progress { get; set; }
    }
}
