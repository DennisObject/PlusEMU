using System.Collections.Immutable;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Talents;

public sealed record TalentTrackSubLevelSnapshot(int AchievementId, int RequiredLevel, string Badge, int State, int Progress, int RequiredProgress);
public sealed record TalentTrackLevelSnapshot(int Level, int State, ImmutableArray<TalentTrackSubLevelSnapshot> SubLevels, ImmutableArray<string> Actions, ImmutableArray<string> Gifts);

public static class TalentTrackSnapshot
{
    public static ImmutableArray<TalentTrackLevelSnapshot> Capture(IEnumerable<TalentTrackLevel> levels, Habbo habbo, IReadOnlyDictionary<string, Achievement> achievements)
    {
        var result = ImmutableArray.CreateBuilder<TalentTrackLevelSnapshot>();
        var unlocked = true;

        foreach (var level in levels.OrderBy(level => level.Level)) {
            var tasks = level.GetSubLevels().OrderBy(sub => sub.Level).Select(sub => Capture(sub, habbo, achievements)).ToImmutableArray();
            var completed = tasks.All(task => task.AchievementId != 0 && task.State == 2);
            var state = unlocked ? completed ? 2 : 1 : 0;
            result.Add(new(level.Level, state,
                tasks.Select(task => task with { State = state == 0 ? 0 : task.State }).ToImmutableArray(),
                level.Actions.ToImmutableArray(), level.Gifts.ToImmutableArray()));
            unlocked &= completed;
        }

        return result.ToImmutable();
    }

    private static TalentTrackSubLevelSnapshot Capture(TalentTrackSubLevel task, Habbo habbo, IReadOnlyDictionary<string, Achievement> achievements)
    {
        foreach (var (group, achievement) in achievements) {
            foreach (var requiredLevel in achievement.Levels.Keys) {
                if (task.Badge != group + requiredLevel) {
                    continue;
                }

                var user = habbo.GetAchievementData(group);
                var completed = user != null && (user.Level >= requiredLevel ||
                    user.Level + 1 == requiredLevel && user.Progress >= task.RequiredProgress);
                var progress = completed ? task.RequiredProgress : user != null && user.Level + 1 == requiredLevel ? Math.Max(0, user.Progress) : 0;

                return new(achievement.Id, requiredLevel, task.Badge, completed ? 2 : 1, progress, task.RequiredProgress);
            }
        }

        // Unknown configured achievements cannot accidentally complete a level.
        return new(0, 0, task.Badge, 0, 0, task.RequiredProgress);
    }
}
