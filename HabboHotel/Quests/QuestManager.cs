using Dapper;
using Plus.Core;
using System.Data;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.Database;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Quests;

public class QuestManager : IQuestManager, IStartable
{
    private readonly IDatabase _database;
    private readonly IMessengerDataLoader _messengerDataLoader;
    private readonly IQuestProgressStore _progressStore;
    private readonly ILogger<QuestManager> _logger;
    private readonly Dictionary<string, int> _questCount;

    private readonly Dictionary<int, Quest> _quests;

    public QuestManager(IDatabase database, IMessengerDataLoader messengerDataLoader, ILogger<QuestManager> logger, IQuestProgressStore progressStore)
    {
        _database = database;
        _messengerDataLoader = messengerDataLoader;
        _logger = logger;
        _progressStore = progressStore;
        _quests = new();
        _questCount = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var quests = await connection.QueryAsync<QuestRow>("SELECT id, type AS Category, level_num AS Number, goal_type AS GoalType, goal_data AS GoalData, action AS Name, pixel_reward AS Reward, data_bit AS DataBit, reward_type AS RewardType, timestamp_unlock AS UnlocksAt, timestamp_lock AS LocksAt FROM quests");
        _quests.Clear();
        _questCount.Clear();
        foreach (var quest in quests)
        {
            _quests.Add(quest.Id, new(quest.Id, quest.Category, quest.Number, (QuestType)quest.GoalType,
                quest.GoalData, quest.Name, quest.Reward, quest.DataBit, quest.RewardType,
                AsUtc(quest.UnlocksAt), AsUtc(quest.LocksAt)));
            AddToCounter(quest.Category);
        }
        _logger.LogInformation("Quest Manager -> LOADED");
    }

    private sealed class QuestRow
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public int Number { get; set; }
        public int GoalType { get; set; }
        public int GoalData { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Reward { get; set; }
        public string DataBit { get; set; } = string.Empty;
        public int RewardType { get; set; }
        public DateTime? UnlocksAt { get; set; }
        public DateTime? LocksAt { get; set; }
    }

    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } date
        ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
        : null;

    private void AddToCounter(string category)
    {
        var count = 0;
        if (_questCount.TryGetValue(category, out count))
            _questCount[category] = count + 1;
        else
            _questCount.Add(category, 1);
    }

    public Quest? GetQuest(int id)
    {
        _quests.TryGetValue(id, out var quest);
        return quest;
    }

    public int GetAmountOfQuestsInCategory(string category)
    {
        _questCount.TryGetValue(category, out var count);
        return count;
    }

    public void ProgressUserQuest(GameClient session, QuestType type, int data = 0)
    {
        if (session == null || session.GetHabbo() == null || session.GetHabbo().HabboStats.QuestId <= 0) return;
        var quest = GetQuest(session.GetHabbo().HabboStats.QuestId);
        if (quest == null || quest.GoalType != type) return;
        var currentProgress = session.GetHabbo().GetQuestProgress(quest.Id);
        var totalProgress = currentProgress;
        var completeQuest = false;
        switch (type)
        {
            default:
                totalProgress++;
                if (totalProgress >= quest.GoalData) completeQuest = true;
                break;
            case QuestType.ExploreFindItem:
                if (data != quest.GoalData)
                    return;
                totalProgress = Convert.ToInt32(quest.GoalData);
                completeQuest = true;
                break;
            case QuestType.StandOn:
                if (data != quest.GoalData)
                    return;
                totalProgress = Convert.ToInt32(quest.GoalData);
                completeQuest = true;
                break;
            case QuestType.XmasParty:
                totalProgress++;
                if (totalProgress == quest.GoalData)
                    completeQuest = true;
                break;
            case QuestType.GiveItem:
                if (data != quest.GoalData)
                    return;
                totalProgress = Convert.ToInt32(quest.GoalData);
                completeQuest = true;
                break;
        }
        _progressStore.SaveProgress(session.GetHabbo().Id, quest.Id, totalProgress, completeQuest);
        session.GetHabbo().Quests[session.GetHabbo().HabboStats.QuestId] = totalProgress;
        session.Send(new QuestStartedComposer(QuestWireDataFactory.Create(session, quest, GetAmountOfQuestsInCategory(quest.Category))));
        if (completeQuest)
        {
            _messengerDataLoader.BroadcastStatusUpdate(session.GetHabbo(), MessengerEventTypes.QuestCompleted, $"{quest.Category}.{quest.Name}");
            session.GetHabbo().HabboStats.QuestId = 0;
            session.GetHabbo().QuestLastCompleted = quest.Id;
            session.Send(new QuestCompletedComposer(QuestWireDataFactory.Create(session, quest, GetAmountOfQuestsInCategory(quest.Category), QuestWireKind.Completed)));
            lock (session.GetHabbo().WalletSync)
            {
                if (!session.GetHabbo().WalletClosed)
                {
                    session.GetHabbo().Duckets += quest.Reward;
                    session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, quest.Reward));
                }
            }
            GetList(session, null);
        }
    }

    public Quest? GetNextQuestInSeries(string category, int number)
    {
        foreach (var quest in _quests.Values)
            if (quest.Category == category && quest.Number == number)
                return quest;
        return null;
    }

    public void GetList(GameClient session, ClientPacket message)
    {
        var userQuestGoals = new Dictionary<string, int>();
        var userQuests = new Dictionary<string, Quest>();
        foreach (var quest in _quests.Values.ToList())
        {
            if (quest.Category.Contains("xmas2012"))
                continue;
            if (!userQuestGoals.ContainsKey(quest.Category))
            {
                userQuestGoals.Add(quest.Category, 1);
                userQuests.Add(quest.Category, null);
            }
            if (quest.Number >= userQuestGoals[quest.Category])
            {
                var userProgress = session.GetHabbo().GetQuestProgress(quest.Id);
                if (session.GetHabbo().HabboStats.QuestId != quest.Id && userProgress >= quest.GoalData) userQuestGoals[quest.Category] = quest.Number + 1;
            }
        }
        foreach (var quest in _quests.Values.ToList())
        {
            foreach (var goal in userQuestGoals)
            {
                if (quest.Category.Contains("xmas2012"))
                    continue;
                if (quest.Category == goal.Key && quest.Number == goal.Value)
                {
                    userQuests[goal.Key] = quest;
                    break;
                }
            }
        }
        var wireQuests = userQuests.Where(entry => entry.Value != null)
            .Select(entry => QuestWireDataFactory.Create(session, entry.Value, GetAmountOfQuestsInCategory(entry.Key)))
            .Concat(userQuests.Where(entry => entry.Value == null).Select(entry => QuestWireDataFactory.Empty(entry.Key)))
            .ToImmutableArray();
        session.Send(new QuestListComposer(new(message != null, wireQuests)));
    }

    public void QuestReminder(GameClient session, int questId)
    {
        var quest = GetQuest(questId);
        if (quest == null)
            return;
        session.Send(new QuestStartedComposer(QuestWireDataFactory.Create(session, quest, GetAmountOfQuestsInCategory(quest.Category))));
    }
}
