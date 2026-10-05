using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.Communication.Flash;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms.AI.Types;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Pets.Commands;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;
using Plus.HabboHotel.Bots;
using Plus.HabboHotel.Rooms.AI.Responses;
using Xunit;

namespace Plus.Tests;

public class BotDomainDependencyTests
{
    [Fact]
    public void FactoryCreatesConcreteAisWithTheirExplicitBehaviorDependencies()
    {
        var locale = new Locale();
        var commands = new Commands();
        var filter = new Filter();
        var bots = new Bots();
        var factory = new BotAiFactory(locale, commands, filter, bots);

        var pet = Assert.IsType<PetBot>(factory.Create(BotAiType.Pet, 3));
        var generic = Assert.IsType<GenericBot>(factory.Create(BotAiType.Generic, 3));
        var bartender = factory.Create(BotAiType.Bartender, 3);

        Assert.Same(locale, Field(pet, "_locale"));
        Assert.Same(commands, Field(pet, "_commands"));
        Assert.Same(filter, Field(generic, "_wordFilter"));
        Assert.Equal("BartenderBot", bartender.GetType().Name);
        Assert.Same(bots, Field(bartender, "_bots"));
        Assert.Same(filter, Field(bartender, "_wordFilter"));
    }

    [Fact]
    public void AttachedPetUsesCurrentOwnerLookupAndRewardsOnlyALevelGain()
    {
        var room = RoomWithUsers(out _);
        var owner = new Client();
        var lookups = 0;
        var clients = new TestGameClientManager(id => { lookups++; Assert.Equal(7, id); return owner; });
        var rewards = new TestRewardProgress();
        var pet = Pet(experience: 99);
        pet.Attach(room, clients, rewards);

        pet.Addexperience(1);
        Assert.Equal(1, lookups);
        Assert.Equal((owner, RewardTrackActions.PetLevel, 1), rewards.Calls.Single());

        pet.Addexperience(1);
        Assert.Equal(1, lookups);
        Assert.Single(rewards.Calls);
    }

    [Fact]
    public void RemovedPetAndAiKeepNoRoomAndCannotRebindAReplacementWithTheSameId()
    {
        var first = RoomWithUsers(out _);
        var replacement = RoomWithUsers(out _);
        var pet = Pet(0);
        pet.RoomId = first.RoomId;
        pet.Attach(first, TestGameClientManager.Empty, TestRewardProgress.Unused);
        var user = new RoomUser(0, first.RoomId, 1, first, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var ai = new TestAi();
        ai.Init(1, 1, first.RoomId, user, first);

        ai.Detach(first, user);
        pet.Detach(first);

        Assert.Null(ai.GetRoom());
        Assert.Null(ai.GetRoomUser());
        Assert.Null(pet.Room);
        Assert.NotSame(replacement, pet.Room);
    }

    private static Pet Pet(int experience) => new(1, 7, 1, "pet", 0, "0", "ffffff", experience, 100, 100, 0,
        null, 0, 0, 0, 0, 0, 0, 0, "", "owner") { VirtualId = 3, ExperienceLevels = [100, 200, 400] };

    private static Room RoomWithUsers(out RoomUserManager users)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var model = new RoomModel("test", 0, 0, 0, 0, "00\r00", 0, 0, false);
        var map = new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty,
            TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        users = new(room, TestRoomUserStore.Instance, TimeProvider.System, TestRewardProgress.Unused,
            TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, users);
        return room;
    }

    private static object? Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private sealed class Locale : IPetLocale { public void Init() { } public string[] GetValue(string key) => [key]; }
    private sealed class Commands : IPetCommandManager { public void Init() { } public int TryInvoke(string input) => input == "sit" ? 3 : 0; }
    private sealed class Filter : IWordFilterManager
    {
        public void Init() { }
        public string CheckMessage(string message) => $"filtered:{message}";
        public bool CheckBannedWords(string message) => false;
        public bool IsFiltered(string message) => false;
    }
    private sealed class Bots : IBotManager
    {
        public Task Init() => Task.CompletedTask;
        public BotResponse? GetResponse(BotAiType type, string message) => new("bartender", message, "served", "say", "");
    }

    private sealed class TestAi : BotAi
    {
        public override void OnSelfEnterRoom() { }
        public override void OnSelfLeaveRoom(bool kicked) { }
        public override void OnUserEnterRoom(RoomUser user) { }
        public override void OnUserLeaveRoom(GameClient client) { }
        public override void OnUserSay(RoomUser user, string message) { }
        public override void OnUserShout(RoomUser user, string message) { }
        public override void OnTimerTick() { }
    }

    private sealed class Client : GameClient
    {
        public Client() : base(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient) { }
        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);
        public override void CreateHeader(Memory<byte> memory, uint messageId) { }
    }
}
