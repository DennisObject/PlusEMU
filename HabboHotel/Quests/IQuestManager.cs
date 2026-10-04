using Plus.Communication.Packets.Incoming;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Quests;

public interface IQuestManager
{
    void Init();
    Quest? GetQuest(int id);
    int GetAmountOfQuestsInCategory(string category);
    void ProgressUserQuest(GameClient session, QuestType type, int data = 0);
    Quest? GetNextQuestInSeries(string category, int number);
    void GetList(GameClient session, ClientPacket message);
    void QuestReminder(GameClient session, int questId);
}