namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>Polaris skill curve: level 1..30 = 1 + floor(sqrt(totalScore / 50)).</summary>
public static class SnowStormSkill
{
    public const int MaxLevel = 30;

    public static int Level(int totalScore) => Math.Min(MaxLevel, 1 + (int)Math.Sqrt(Math.Max(0, totalScore) / 50));

    public static int ScoreToNextLevel(int totalScore)
    {
        var level = Level(totalScore);

        return level >= MaxLevel ? 0 : 50 * level * level - Math.Max(0, totalScore);
    }
}
