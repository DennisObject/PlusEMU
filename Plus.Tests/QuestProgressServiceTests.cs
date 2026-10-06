using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class QuestProgressServiceTests
{
    [Fact]
    public void StartCommitsBeforePublishingQuestStateAndPackets()
    {
        var quest = TestQuest(3, "social", 1);
        var manager = new QuestManagerFake(quest);
        var (client, sent) = Client(7);
        var store = new RecordingStore(() => Assert.Equal(0, client.GetHabbo().HabboStats.QuestId));

        new QuestProgressService(store, manager).Start(client, quest.Id);

        Assert.Equal((7, quest.Id), Assert.Single(store.Starts));
        Assert.Equal(quest.Id, client.GetHabbo().HabboStats.QuestId);
        Assert.Equal(1, manager.ListRequests);
        Assert.Single(sent);
    }

    [Fact]
    public void PersistenceFailureDoesNotPublishStart()
    {
        var quest = TestQuest(3, "social", 1);
        var manager = new QuestManagerFake(quest);
        var (client, sent) = Client(7);
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new QuestProgressService(store, manager).Start(client, quest.Id));
        Assert.Equal(0, client.GetHabbo().HabboStats.QuestId);
        Assert.Equal(0, manager.ListRequests);
        Assert.Empty(sent);
    }

    [Fact]
    public void CancelFailureLeavesActiveQuestPublished()
    {
        var quest = TestQuest(3, "social", 1);
        var manager = new QuestManagerFake(quest);
        var (client, sent) = Client(7, quest.Id);
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new QuestProgressService(store, manager).Cancel(client));
        Assert.Equal(quest.Id, client.GetHabbo().HabboStats.QuestId);
        Assert.Equal(0, manager.ListRequests);
        Assert.Empty(sent);
    }

    [Fact]
    public void StartNextRequiresRoomAndSelectsNextQuestInSeries()
    {
        var completed = TestQuest(3, "social", 1);
        var next = TestQuest(4, "social", 2);
        var manager = new QuestManagerFake(completed, next);
        var (client, _) = Client(7);
        client.GetHabbo().QuestLastCompleted = completed.Id;
        var store = new RecordingStore();
        var service = new QuestProgressService(store, manager);

        service.StartNext(client);
        Assert.Empty(store.Starts);

        client.GetHabbo().CurrentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        service.StartNext(client);

        Assert.Equal((7, next.Id), Assert.Single(store.Starts));
        Assert.Equal((completed.Category, completed.Number + 1), manager.NextRequest);
    }

    [Fact]
    public void ProgressPersistenceFailureLeavesMemoryAndPacketsUnchanged()
    {
        var quest = new Quest(3, "social", 1, QuestType.SocialChat, 5, "chat", 5, "", 3, null, null);
        var store = new RecordingStore { Fail = true };
        var manager = new QuestManager(null!, null!, NullLogger<QuestManager>.Instance, store);
        var loaded = (Dictionary<int, Quest>)typeof(QuestManager).GetField("_quests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        loaded.Add(quest.Id, quest);
        var (client, sent) = Client(7, quest.Id);
        client.GetHabbo().Quests[quest.Id] = 1;

        Assert.Throws<InvalidOperationException>(() => manager.ProgressUserQuest(client, QuestType.SocialChat));

        Assert.Equal(1, client.GetHabbo().Quests[quest.Id]);
        Assert.Equal(quest.Id, client.GetHabbo().HabboStats.QuestId);
        Assert.Empty(sent);
    }

    private static (FlashGameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(int userId, int questId = 0)
    {
        var stats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, questId, 0, 0, "", 0);

        return HabbiconTestSupport.Client(new() { Id = userId, HabboStats = stats });
    }

    private static Quest TestQuest(int id, string category, int number) =>
        new(id, category, number, QuestType.SocialChat, 1, "chat", 5, "", 3, null, null);

    private sealed class RecordingStore(Action? beforeStart = null) : IQuestProgressStore
    {
        public bool Fail { get; init; }
        public List<(int UserId, int QuestId)> Starts { get; } = [];
        public List<(int UserId, int QuestId, int Progress, bool Completed)> Progress { get; } = [];
        public void Start(int userId, int questId)
        {
            beforeStart?.Invoke();

            if (Fail) {
                throw new InvalidOperationException("forced failure");
            }

            Starts.Add((userId, questId));
        }
        public void Cancel(int userId, int questId)
        {
            if (Fail) {
                throw new InvalidOperationException("forced failure");
            }
        }
        public void SaveProgress(int userId, int questId, int progress, bool completed)
        {
            if (Fail) {
                throw new InvalidOperationException("forced failure");
            }

            Progress.Add((userId, questId, progress, completed));
        }
    }

    private sealed class QuestManagerFake(params Quest[] quests) : IQuestManager
    {
        private readonly Dictionary<int, Quest> _quests = quests.ToDictionary(quest => quest.Id);
        public int ListRequests { get; private set; }
        public (string Category, int Number)? NextRequest { get; private set; }
        public Quest GetQuest(int id) => _quests.GetValueOrDefault(id)!;
        public Quest GetNextQuestInSeries(string category, int number)
        {
            NextRequest = (category, number);

            return _quests.Values.SingleOrDefault(quest => quest.Category == category && quest.Number == number)!;
        }
        public void GetList(GameClient session, ClientPacket message) => ListRequests++;
        public int GetAmountOfQuestsInCategory(string category) => _quests.Values.Count(quest => quest.Category == category);
        public void Init()
        {
        }
        public void ProgressUserQuest(GameClient session, QuestType type, int data = 0) => throw new NotSupportedException();
        public void QuestReminder(GameClient session, int questId) => throw new NotSupportedException();
    }
}
