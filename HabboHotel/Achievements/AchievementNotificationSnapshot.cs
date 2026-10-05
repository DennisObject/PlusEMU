using System.Globalization;

namespace Plus.HabboHotel.Achievements;

public static class AchievementNotificationSnapshot
{
    public static AchievementProgressSnapshot CaptureProgress(Achievement achievement, int targetLevel,
        AchievementLevel level, int totalLevels, UserAchievement? user) => new(
        achievement.Id, achievement.Category ?? string.Empty,
        (achievement.GroupName ?? string.Empty) + targetLevel.ToString(CultureInfo.InvariantCulture),
        targetLevel, level.Requirement, level.RewardPixels, user?.Progress ?? 0,
        user != null && user.Level >= totalLevels, totalLevels);
}

public sealed record AchievementUnlockSnapshot(int Id, int Level, string Badge, string PreviousBadge,
    int PointReward, int PixelReward, string Category)
{
    public static AchievementUnlockSnapshot Capture(Achievement achievement, int level, int pointReward, int pixelReward)
    {
        var name = achievement.GroupName ?? string.Empty;
        return new(achievement.Id, level, name + level.ToString(CultureInfo.InvariantCulture),
            level > 1 ? name + (level - 1).ToString(CultureInfo.InvariantCulture) : string.Empty,
            pointReward, pixelReward, achievement.Category ?? string.Empty);
    }
}
