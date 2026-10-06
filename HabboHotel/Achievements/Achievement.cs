namespace Plus.HabboHotel.Achievements;

public class Achievement
{

    public int Id { get; set; }

    public string? Category { get; set; }

    public string? GroupName { get; set; }

    public int RewardPixels { get; set; }

    public int RewardPoints { get; set; }

    public int ProgressNeeded { get; set; }

    public int GameId { get; set; }

    public int Level { get; set; }

    public Dictionary<int, AchievementLevel> Levels { get; set; } = new();

    public void AddLevel(AchievementLevel level) => Levels.Add(level.Level, level);
}
