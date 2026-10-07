using System.Globalization;
using Plus.HabboHotel.Badges.Rarity;

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
    int PointReward, int PixelReward, string Category, BadgeRarity Rarity)
{
    public static AchievementUnlockSnapshot Capture(Achievement achievement, int level, int pointReward, int pixelReward,
        BadgeRarityTable? rarity = null)
    {
        var name = achievement.GroupName ?? string.Empty;
        var badge = name + level.ToString(CultureInfo.InvariantCulture);

        return new(achievement.Id, level, badge,
            level > 1 ? name + (level - 1).ToString(CultureInfo.InvariantCulture) : string.Empty,
            pointReward, pixelReward, achievement.Category ?? string.Empty, (rarity ?? BadgeRarityTable.Current).Get(badge));
    }
}
