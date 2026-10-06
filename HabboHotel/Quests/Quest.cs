namespace Plus.HabboHotel.Quests;

public class Quest
{
    public Quest(int id, string category, int number, QuestType goalType, int goalData, string name,
        int reward, string dataBit, int rewardType, DateTimeOffset? unlocksAt, DateTimeOffset? locksAt)
    {
        Id = id;
        Category = category;
        Number = number;
        GoalType = goalType;
        GoalData = goalData;
        Name = name;
        Reward = reward;
        DataBit = dataBit;
        RewardType = rewardType;
        UnlocksAt = unlocksAt?.ToUniversalTime();
        LocksAt = locksAt?.ToUniversalTime();
    }

    public int Id { get; }
    public string Category { get; }
    public string DataBit { get; }
    public int GoalData { get; }
    public QuestType GoalType { get; }
    public string Name { get; }
    public int Number { get; }
    public int Reward { get; }
    public int RewardType { get; }
    public DateTimeOffset? UnlocksAt { get; }
    public DateTimeOffset? LocksAt { get; }

    public string ActionName => QuestTypeUtillity.GetString(GoalType);

    public bool IsEndedAt(DateTimeOffset now) => LocksAt is { } locksAt && now >= locksAt;

    public bool IsCompleted(int progress)
    {
        switch (GoalType) {
            default:
                return progress >= GoalData;
            case QuestType.ExploreFindItem:
                return progress >= 1;
        }
    }
}
