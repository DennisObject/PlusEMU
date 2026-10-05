using System.Collections.Immutable;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Achievements;

// The next level a user is working towards, already resolved against the achievement's own levels.
public sealed record AchievementProgressSnapshot(int Id, string Category, string Badge, int TargetLevel, int Requirement, int RewardPixels,
    int Progress, bool Completed, int TotalLevels);

public interface IAchievementSnapshotService
{
    ImmutableArray<AchievementProgressSnapshot> Capture(Habbo habbo, IEnumerable<Achievement> achievements);
}

public sealed class AchievementSnapshotService : IAchievementSnapshotService
{
    public ImmutableArray<AchievementProgressSnapshot> Capture(Habbo habbo, IEnumerable<Achievement> achievements)
    {
        var snapshots = ImmutableArray.CreateBuilder<AchievementProgressSnapshot>();
        foreach (var achievement in achievements)
        {
            if (Capture(habbo, achievement) is { } snapshot)
                snapshots.Add(snapshot);
        }
        return snapshots.ToImmutable();
    }

    private static AchievementProgressSnapshot? Capture(Habbo habbo, Achievement achievement)
    {
        // An achievement without levels has no next level to show, so it is left out instead of being indexed.
        if (achievement.Levels.Count == 0) return null;
        var group = achievement.GroupName ?? string.Empty;
        var progress = habbo.GetAchievementData(group);
        var totalLevels = achievement.Levels.Count;
        // The next level, clamped to the highest level the achievement defines; a completed achievement stays on its last level.
        var target = (int)Math.Clamp((long)(progress?.Level ?? 0) + 1, 1, achievement.Levels.Keys.Max());
        var (level, data) = ResolveLevel(achievement.Levels, target);
        return new AchievementProgressSnapshot(
            achievement.Id,
            achievement.Category ?? string.Empty,
            group + level,
            level,
            data.Requirement,
            data.RewardPixels,
            progress?.Progress ?? 0,
            progress != null && progress.Level >= totalLevels,
            totalLevels);
    }

    // Sparse levels: the target when defined, otherwise the nearest defined level above it, otherwise the highest one below it.
    private static (int Level, AchievementLevel Data) ResolveLevel(Dictionary<int, AchievementLevel> levels, int target)
    {
        if (levels.TryGetValue(target, out var exact))
            return (target, exact);
        var level = levels.Keys.Where(key => key >= target).DefaultIfEmpty(levels.Keys.Max()).Min();
        return (level, levels[level]);
    }
}
