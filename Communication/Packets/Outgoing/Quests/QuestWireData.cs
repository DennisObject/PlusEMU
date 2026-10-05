using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed record QuestWireData(string Category, int CategoryProgress, int CategoryTotal, int RewardType, int Id, bool Started,
    string ActionName, string DataBit, int Reward, string Name, int UserProgress, int Goal, int TimeUnlock);

public sealed record QuestListData(bool Send, ImmutableArray<QuestWireData> Quests);

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
