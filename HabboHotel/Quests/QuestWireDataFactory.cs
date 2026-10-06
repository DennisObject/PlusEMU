using Plus.Communication.Packets.Outgoing.Quests;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Quests;

public enum QuestWireKind { Started, Completed }

public static class QuestWireDataFactory
{
    public static QuestWireData Create(GameClient session, Quest quest, int categoryTotal, QuestWireKind kind = QuestWireKind.Started)
    {
        var progress = session.GetHabbo().GetQuestProgress(quest.Id);
        var categoryProgress = kind == QuestWireKind.Completed ? quest.Number : quest.Number - 1;
        if (kind == QuestWireKind.Started && quest.IsCompleted(progress)) categoryProgress++;
        var total = kind == QuestWireKind.Completed && quest.Name.Contains("xmas2012") ? 1 : categoryTotal;
        if (kind == QuestWireKind.Started && quest.Category.Contains("xmas2012")) { categoryProgress = 0; total = 0; }
        return new(quest.Category, categoryProgress, total, quest.RewardType, quest.Id,
            session.GetHabbo().HabboStats.QuestId == quest.Id, quest.ActionName, quest.DataBit, quest.Reward,
            quest.Name, progress, quest.GoalData, LegacyTimestamp(quest.UnlocksAt));
    }

    public static QuestWireData Empty(string category) => new(category, 0, 0, 3, 0, false, "", "", 0, "", 0, 0, 0);

    private static int LegacyTimestamp(DateTimeOffset? value) => value is { } timestamp
        ? (int)Math.Clamp(timestamp.ToUnixTimeSeconds(), 0, int.MaxValue)
        : 0;
}
