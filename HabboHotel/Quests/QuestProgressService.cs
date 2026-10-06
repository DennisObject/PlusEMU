using Dapper;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Quests;

public interface IQuestProgressStore
{
    void Start(int userId, int questId);
    void Cancel(int userId, int questId);
    void SaveProgress(int userId, int questId, int progress, bool completed);
}

public sealed class QuestProgressStore(IDatabase database) : IQuestProgressStore
{
    public void Start(int userId, int questId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("REPLACE INTO user_quests (user_id,quest_id) VALUES (@userId,@questId)", new { userId, questId }, transaction);
        if (connection.Execute("UPDATE user_statistics SET quest_id=@questId WHERE id=@userId LIMIT 1", new { userId, questId }, transaction) != 1)
            throw new InvalidOperationException("Quest state was not persisted.");
        transaction.Commit();
    }

    public void Cancel(int userId, int questId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM user_quests WHERE user_id=@userId AND quest_id=@questId", new { userId, questId }, transaction);
        if (connection.Execute("UPDATE user_statistics SET quest_id=0 WHERE id=@userId LIMIT 1", new { userId }, transaction) != 1)
            throw new InvalidOperationException("Quest cancellation was not persisted.");
        transaction.Commit();
    }

    public void SaveProgress(int userId, int questId, int progress, bool completed)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Execute("UPDATE user_quests SET progress=@progress WHERE user_id=@userId AND quest_id=@questId LIMIT 1",
                new { userId, questId, progress }, transaction) != 1)
            throw new InvalidOperationException("Quest progress was not persisted.");
        if (completed && connection.Execute("UPDATE user_statistics SET quest_id=0 WHERE id=@userId LIMIT 1", new { userId }, transaction) != 1)
            throw new InvalidOperationException("Quest completion was not persisted.");
        transaction.Commit();
    }
}

public interface IQuestProgressService
{
    void Start(GameClient session, int questId);
    void Cancel(GameClient session);
    void StartNext(GameClient session);
}

public sealed class QuestProgressService(IQuestProgressStore store, IQuestManager quests) : IQuestProgressService
{
    public void Start(GameClient session, int questId)
    {
        var quest = quests.GetQuest(questId);
        if (quest == null) return;
        PublishStarted(session, quest);
    }

    public void Cancel(GameClient session)
    {
        var habbo = session.GetHabbo();
        var quest = quests.GetQuest(habbo.HabboStats.QuestId);
        if (quest == null) return;
        store.Cancel(habbo.Id, quest.Id);
        habbo.HabboStats.QuestId = 0;
        session.Send(new QuestAbortedComposer());
        quests.GetList(session, null);
    }

    public void StartNext(GameClient session)
    {
        var habbo = session.GetHabbo();
        if (!habbo.InRoom) return;
        var completed = quests.GetQuest(habbo.QuestLastCompleted);
        if (completed == null) return;
        var next = quests.GetNextQuestInSeries(completed.Category, completed.Number + 1);
        if (next == null) return;
        PublishStarted(session, next);
    }

    private void PublishStarted(GameClient session, Quest quest)
    {
        var habbo = session.GetHabbo();
        store.Start(habbo.Id, quest.Id);
        habbo.HabboStats.QuestId = quest.Id;
        quests.GetList(session, null);
        session.Send(new QuestStartedComposer(QuestWireDataFactory.Create(session, quest, quests.GetAmountOfQuestsInCategory(quest.Category))));
    }
}
