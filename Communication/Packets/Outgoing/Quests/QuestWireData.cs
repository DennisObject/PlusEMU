using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Outgoing.Quests;

public enum QuestWireKind { Started, Completed }

public sealed record QuestWireData(string Category, int CategoryProgress, int CategoryTotal, int RewardType, int Id, bool Started,
    string ActionName, string DataBit, int Reward, string Name, int UserProgress, int Goal, int TimeUnlock);

public sealed record QuestListData(bool Send, ImmutableArray<QuestWireData> Quests);

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
            session.GetHabbo().HabboStats.QuestId == quest.Id, quest.ActionName, quest.DataBit, quest.Reward, quest.Name, progress, quest.GoalData, quest.TimeUnlock);
    }

    public static QuestWireData Empty(string category) => new(category, 0, 0, 3, 0, false, "", "", 0, "", 0, 0, 0);
}

internal static class QuestWireSerializer
{
    public static void Write(IOutgoingPacket packet, QuestWireData quest)
    {
        packet.WriteString(quest.Category);
        packet.WriteInteger(quest.CategoryProgress);
        packet.WriteInteger(quest.CategoryTotal);
        packet.WriteInteger(quest.RewardType);
        packet.WriteInteger(quest.Id);
        packet.WriteBoolean(quest.Started);
        packet.WriteString(quest.ActionName);
        packet.WriteString(quest.DataBit);
        packet.WriteInteger(quest.Reward);
        packet.WriteString(quest.Name);
        packet.WriteInteger(quest.UserProgress);
        packet.WriteInteger(quest.Goal);
        packet.WriteInteger(quest.TimeUnlock);
        packet.WriteString("");
        packet.WriteString("");
        packet.WriteBoolean(true);
    }
}
