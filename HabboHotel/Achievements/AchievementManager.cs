using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Achievements;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;
using Plus.HabboHotel.Users;
using Plus.Core;

namespace Plus.HabboHotel.Achievements;

public class AchievementManager : IAchievementManager, IStartable
{
    public Dictionary<string, Achievement> Achievements { get; private set; }
    private readonly IAchievementLevelFactory _achievementLevelFactory;
    private readonly IDatabase _database;
    private readonly IBadgeManager _badgeManager;

    public AchievementManager(IAchievementLevelFactory achievementLevelFactory, IDatabase database, IBadgeManager badgeManager)
    {
        _achievementLevelFactory = achievementLevelFactory;
        _database = database;
        _badgeManager = badgeManager;
        Achievements = new();
    }

    public async Task Start() => await Init();

    public async Task Init() => Achievements = await _achievementLevelFactory.GetAchievementLevels();

    public bool ProgressAchievement(GameClient session, string group, int progress, bool fromBeginning = false)
    {
        if (!Achievements.ContainsKey(group) || session == null)
            return false;
        var data = Achievements[group];
        if (data == null) return false;
        var userData = session.GetHabbo().GetAchievementData(group);
        if (userData == null)
        {
            userData = new(group, 0, 0);
            session.GetHabbo().Achievements.TryAdd(group, userData);
        }
        var totalLevels = data.Levels.Count;
        if (userData.Level == totalLevels)
            return false; // done, no more.
        var targetLevel = userData.Level + 1;
        if (targetLevel > totalLevels)
            targetLevel = totalLevels;
        var level = data.Levels[targetLevel];
        int newProgress;
        if (fromBeginning)
            newProgress = progress;
        else
            newProgress = userData.Progress + progress;
        var newLevel = userData.Level;
        var newTarget = newLevel + 1;
        if (newTarget > totalLevels)
            newTarget = totalLevels;
        if (newProgress >= level.Requirement)
        {
            newLevel++;
            newTarget++;
            newProgress = 0;
            if (targetLevel != 1)
                session.GetHabbo().Inventory.Badges.RemoveBadge(Convert.ToString(group + (targetLevel - 1)));
            _badgeManager.GiveBadge(session.GetHabbo(), group + targetLevel).Wait();
            if (newTarget > totalLevels) newTarget = totalLevels;
            session.Send(new AchievementUnlockedComposer(AchievementUnlockSnapshot.Capture(data, targetLevel, level.RewardPoints, level.RewardPixels)));
            BroadcastAchievement(session.GetHabbo(), MessengerEventTypes.AchievementUnlocked, group + targetLevel);

            using (var connection = _database.Connection())
            {
                connection.Execute("REPLACE INTO `user_achievements` VALUES (@habboId, @group, @newLevel, @newProgress)",
                    new { habboId = session.GetHabbo().Id, group, newLevel, newProgress });
            }

            userData.Level = newLevel;
            userData.Progress = newProgress;
            lock (session.GetHabbo().WalletSync)
            {
                if (!session.GetHabbo().WalletClosed)
                {
                    session.GetHabbo().Duckets += level.RewardPixels;
                    session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, level.RewardPixels));
                }
            }
            session.GetHabbo().HabboStats.AchievementPoints += level.RewardPoints;
            session.Send(new AchievementScoreComposer(session.GetHabbo().HabboStats.AchievementPoints));
            var newLevelData = data.Levels[newTarget];
            session.Send(new AchievementProgressedComposer(AchievementNotificationSnapshot.CaptureProgress(data, newTarget, newLevelData, totalLevels, session.GetHabbo().GetAchievementData(group))));
            return true;
        }
        userData.Level = newLevel;
        userData.Progress = newProgress;

        using (var connection = _database.Connection())
        {
            connection.Execute("REPLACE INTO `user_achievements` VALUES (@habboId, @group, @newLevel, @newProgress)",
                new { habboId = session.GetHabbo().Id, group, newLevel, newProgress });
        }

        session.Send(new AchievementProgressedComposer(AchievementNotificationSnapshot.CaptureProgress(data, targetLevel, level, totalLevels, session.GetHabbo().GetAchievementData(group))));
        return false;
    }

    public ICollection<Achievement> GetGameAchievements(int gameId)
    {
        var achievements = new List<Achievement>();
        foreach (var achievement in Achievements.Values.ToList())
        {
            if (achievement.Category == "games" && achievement.GameId == gameId)
                achievements.Add(achievement);
        }
        return achievements;
    }

    public void BroadcastAchievement(Habbo habbo, MessengerEventTypes eventType, string level)
    {
    }
}