using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Packets.Incoming.Habbicons;
using Plus.Communication.Packets.Incoming.Rooms.Chat;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Logs;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Ignores;
using Xunit;

namespace Plus.Tests;

public sealed class RoomChatServiceTests
{
    private static readonly DateTimeOffset Now = new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task HandlersDecodeFullPrimitiveRequestsAndDelegateOnly()
    {
        var service = new RecordingRoomChatService();
        GameClient session = null!;
        await new ChatEvent(service).Parse(session, HabbiconTestSupport.Incoming("chat", 2));
        await new ShoutEvent(service).Parse(session, HabbiconTestSupport.Incoming("shout", 3));
        await new WhisperEvent(service).Parse(session, HabbiconTestSupport.Incoming("Bob private words", 4));

        Assert.Equal(("chat", 2), service.ChatRequest);
        Assert.Equal(("shout", 3), service.ShoutRequest);
        Assert.Equal(("Bob private words", 4), service.WhisperRequest);
    }

    [Fact]
    public async Task FloodDenialStopsLoggingCommandsAndPublication()
    {
        var world = new World(new ZonedClock(Now, TimeZoneInfo.Utc));
        world.Sender.GetHabbo().FloodUntil = Now.AddSeconds(1);

        await world.Service.Chat(world.Sender, "blocked", 1);

        Assert.Empty(world.Logs.Entries);
        Assert.Empty(world.Commands.Messages);
        Assert.Empty(world.SenderPackets);
        Assert.Empty(world.Quests.Progresses);
        Assert.Empty(world.Rewards.Progresses);
    }

    [Fact]
    public async Task CommandIsLoggedButDoesNotPublishOrProgress()
    {
        var world = new World();
        world.Commands.Handled = true;

        await world.Service.Chat(world.Sender, ":wave", 1);

        var entry = Assert.Single(world.Logs.Entries);
        Assert.Equal(":wave", entry.Message);
        Assert.Equal(Now, entry.CreatedAt);
        Assert.Equal(new[] { ":wave" }, world.Commands.Messages);
        Assert.Empty(world.SenderPackets);
        Assert.Empty(world.Quests.Progresses);
        Assert.Empty(world.Rewards.Progresses);
    }

    [Fact]
    public async Task BannedNormalAndShoutPublishTheirExactPacketKindsWithoutQuestProgress()
    {
        var world = new World();
        world.Filter.Banned = true;

        await world.Service.Chat(world.Sender, "blocked normal", 2);
        await world.Service.Shout(world.Sender, "blocked shout", 3);

        Assert.Equal(2, world.Logs.Entries.Count);
        Assert.Equal(Now, world.Logs.Entries[0].CreatedAt);
        Assert.Equal(Now, world.Logs.Entries[1].CreatedAt);
        Assert.Equal(2, world.SenderPackets.Count);
        Assert.Contains("blocked normal", Text(world.SenderPackets[0].Payload));
        Assert.Contains("blocked shout", Text(world.SenderPackets[1].Payload));
        Assert.Empty(world.Quests.Progresses);
        Assert.Empty(world.Rewards.Progresses);
    }

    [Fact]
    public async Task WhisperSplitsCombinedFieldFiltersAndPublishesToBothUsers()
    {
        var world = new World();
        world.Filter.Replacement = "filtered words";

        await world.Service.Whisper(world.Sender, "Bob secret words", 2);

        var entry = Assert.Single(world.Logs.Entries);
        Assert.Equal("<Whisper to Bob>: filtered words", entry.Message);
        Assert.Equal(Now, entry.CreatedAt);
        Assert.Single(world.SenderPackets);
        Assert.Single(world.RecipientPackets);
        Assert.Contains("filtered words", Text(world.SenderPackets[0].Payload));
        Assert.Contains("filtered words", Text(world.RecipientPackets[0].Payload));
        Assert.Single(world.Quests.Progresses);
        Assert.Single(world.Rewards.Progresses);
    }

    [Fact]
    public async Task FilteredNormalAndShoutReachWiredBeforeVisibleChatPublication()
    {
        var world = new World();
        world.Filter.Replacement = "filtered words";
        world.AddHiddenSpeechTrigger("filtered words");

        await world.Service.Chat(world.Sender, "normal source", 2);
        await world.Service.Shout(world.Sender, "shout source", 3);

        Assert.Equal(2, world.Logs.Entries.Count);
        Assert.Equal(2, world.Quests.Progresses.Count);
        Assert.Empty(world.SenderPackets);
        Assert.False(world.Sender.GetHabbo().HasSpoken);
    }

    [Fact]
    public async Task UtcFloodDeadlinePreservesBeforeExactAndAfterBoundariesInNonUtcZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("chat-test-plus-two", TimeSpan.FromHours(2), "test", "test");
        var deadlineUtc = Now.AddMilliseconds(500);
        var before = new World(new ZonedClock(deadlineUtc.AddMilliseconds(-1), zone));
        before.Sender.GetHabbo().FloodUntil = deadlineUtc;
        before.Commands.Handled = true;
        await before.Service.Chat(before.Sender, ":before", 1);
        Assert.Empty(before.Logs.Entries);

        var exact = new World(new ZonedClock(deadlineUtc, zone));
        exact.Sender.GetHabbo().FloodUntil = deadlineUtc;
        exact.Commands.Handled = true;
        await exact.Service.Chat(exact.Sender, ":exact", 1);
        Assert.Single(exact.Logs.Entries);

        var after = new World(new ZonedClock(deadlineUtc.AddMilliseconds(1), zone));
        after.Sender.GetHabbo().FloodUntil = deadlineUtc;
        after.Commands.Handled = true;
        await after.Service.Chat(after.Sender, ":after", 1);
        Assert.Single(after.Logs.Entries);
    }

    [Fact]
    public void SixthMessageSetsAndRefreshesUtcFloodDeadline()
    {
        var world = new World();
        var first = Now.AddMilliseconds(125);

        for (var i = 0; i < 5; i++)
            Assert.False(world.SenderUser.IncrementAndCheckFlood(first, out _));
        Assert.True(world.SenderUser.IncrementAndCheckFlood(first, out var firstMute));
        Assert.Equal(first.AddSeconds(firstMute), world.Sender.GetHabbo().FloodUntil);

        var refreshed = first.AddMinutes(1);
        for (var i = 0; i < 5; i++)
            Assert.False(world.SenderUser.IncrementAndCheckFlood(refreshed, out _));
        Assert.True(world.SenderUser.IncrementAndCheckFlood(refreshed, out var refreshedMute));
        Assert.Equal(refreshed.AddSeconds(refreshedMute), world.Sender.GetHabbo().FloodUntil);
    }

    [Fact]
    public async Task HabbiconTriggerUsesOneUtcInstantForSixthMessageDeadline()
    {
        var now = Now.AddMilliseconds(375);
        var clock = new ZonedClock(now, TimeZoneInfo.CreateCustomTimeZone("habbicon-plus-nine", TimeSpan.FromHours(9), "test", "test"));
        var world = new World(clock);
        world.SenderUser.ChatSpamCount = 5;
        world.Sender.GetHabbo().LastHabbiconTrigger = Environment.TickCount64 - 2000;
        clock.Calls = 0;

        await new TriggerHabbiconEvent(new HabbiconTestSupport.Service(), clock)
            .Parse(world.Sender, HabbiconTestSupport.Incoming(61));

        Assert.Equal(1, clock.Calls);
        Assert.NotNull(world.Sender.GetHabbo().FloodUntil);
        Assert.Equal(now, world.Sender.GetHabbo().FloodUntil!.Value.AddSeconds(-20));
    }

    [Fact]
    public async Task BannedThresholdUsesSameSampledUtcInstantForExpiry()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("chat-test-plus-five-thirty", TimeSpan.FromHours(5.5), "test", "test");
        var now = Now.AddMilliseconds(789);
        var (moderation, recorder) = RecordingModeration.Create();
        var clock = new ZonedClock(now, zone);
        var world = new World(clock, moderation);
        world.Filter.Banned = true;
        world.Settings.Chances = 1;
        clock.Calls = 0;

        await world.Service.Chat(world.Sender, "threshold", 1);

        Assert.Equal(now.AddSeconds(78892200), recorder.Expiry);
        Assert.Equal(now, Assert.Single(world.Logs.Entries).CreatedAt);
        Assert.Equal(1, clock.Calls);
    }

    [Fact]
    public async Task MalformedBotDisconnectedOrDepartedWhisperRecipientDoesNotPublish()
    {
        var world = new World();

        await world.Service.Whisper(world.Sender, "Bob", 1);
        Assert.Empty(world.Logs.Entries);
        Assert.Empty(world.SenderPackets);
        Assert.Empty(world.RecipientPackets);

        world.RecipientUser.BotData = (Plus.HabboHotel.Rooms.AI.RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(Plus.HabboHotel.Rooms.AI.RoomBot));
        await world.Service.Whisper(world.Sender, "Bob message", 1);
        Assert.Empty(world.Logs.Entries);
        Assert.Empty(world.SenderPackets);
        Assert.Empty(world.RecipientPackets);

        world.RecipientUser.BotData = null!;
        world.Clients.ByUserId.Remove(8);
        await world.Service.Whisper(world.Sender, "Bob message", 1);
        Assert.Empty(world.Logs.Entries);
        Assert.Empty(world.SenderPackets);
        Assert.Empty(world.RecipientPackets);

        world.Clients.ByUserId[8] = world.Recipient;
        world.Recipient.GetHabbo().CurrentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        await world.Service.Whisper(world.Sender, "Bob message", 1);
        Assert.Empty(world.Logs.Entries);
        Assert.Empty(world.SenderPackets);
        Assert.Empty(world.RecipientPackets);
    }

    private static string Text(byte[] packet) => Encoding.UTF8.GetString(packet);

    private sealed class World
    {
        public GameClient Sender { get; }
        public GameClient Recipient { get; }
        public RoomUser RecipientUser { get; }
        public RoomUser SenderUser { get; }
        public List<(uint Header, byte[] Payload)> SenderPackets { get; }
        public List<(uint Header, byte[] Payload)> RecipientPackets { get; }
        public RecordingLogs Logs { get; } = new();
        public RecordingCommands Commands { get; } = new();
        public RecordingFilter Filter { get; } = new();
        public RecordingQuests Quests { get; } = new();
        public RecordingRewards Rewards { get; } = new();
        public Settings Settings { get; } = new();
        public ClientDirectory Clients { get; }
        public IRoomChatService Service { get; }
        private readonly Room _room;
        private readonly WiredComponent _wired;
        private readonly ConcurrentDictionary<uint, Item> _floorItems;
        private uint _nextItemId = 1;

        public World(TimeProvider? clock = null, IModerationManager? moderation = null)
        {
            clock ??= new FixedClock(Now);
            _room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            _room.Id = 42;
            _room.MutedUsers = [];
            _room.WordFilterList = [];
            var users = new RoomUserManager(_room, TestRoomUserStore.Instance, clock);
            var items = new RoomItemHandling(_room, TestRoomItemStore.Instance);
            _floorItems = (ConcurrentDictionary<uint, Item>)Get(items, "_floorItems");
            _wired = new WiredComponent(_room, TestLogging.Logger, clock);
            Set(_room, "_roomUserManager", users);
            Set(_room, "_roomItemHandling", items);
            Set(_room, "_wiredComponent", _wired);

            (Sender, SenderPackets) = Client(new Habbo
            {
                Id = 7, Username = "Alice", CurrentRoom = _room, Effects = new EffectsComponent(clock),
                IgnoresComponent = new([]), ReceiveWhispers = true
            });
            (Recipient, RecipientPackets) = Client(new Habbo
            {
                Id = 8, Username = "Bob", CurrentRoom = _room, Effects = new EffectsComponent(clock),
                IgnoresComponent = new([]), ReceiveWhispers = true
            });
            SenderUser = new RoomUser(7, 1, 11, _room);
            Add(users, SenderUser, Sender);
            RecipientUser = new RoomUser(8, 2, 12, _room);
            Add(users, RecipientUser, Recipient);
            var clientManager = ClientDirectory.Create(out var clients);
            Clients = clients;
            Clients.ByUserId[7] = Sender;
            Clients.ByUserId[8] = Recipient;

            Service = new RoomChatService(new Styles(), Logs, Filter, Commands,
                moderation ?? DefaultProxy<IModerationManager>.Create(), Settings, Quests, Rewards, clientManager, clock);
        }

        public void AddHiddenSpeechTrigger(string message)
        {
            var item = new Item
            {
                Id = _nextItemId++, ExtraData = new LegacyDataFormat { Data = "1" },
                Definition = new() { ItemName = "wf_trg_says_something" }
            };
            _floorItems[item.Id] = item;
            var trigger = _wired.CreateConfiguredBox(item)!;
            Assert.True(trigger.TryValidateConfiguration(new()
                { Text = message, IntParams = [1, 1, 0] }, out var configuration, out var error), error);
            trigger.ApplyConfiguration(configuration);
            Assert.True(_wired.AddBox(trigger));
        }

        private static (GameClient Client, List<(uint Header, byte[] Payload)> Packets) Client(Habbo habbo)
        {
            var (client, sent) = HabbiconTestSupport.Client(habbo);
            habbo.Client = client;
            return (client, sent);
        }

        private static void Add(RoomUserManager manager, RoomUser user, GameClient client)
        {
            user.UserId = client.GetHabbo().Id;
            Set(user, "_mClient", client);
            var users = (ConcurrentDictionary<int, RoomUser>)Get(manager, "_users");
            users[user.VirtualId] = user;
        }
    }

    private sealed class RecordingRoomChatService : IRoomChatService
    {
        public (string Message, int Colour) ChatRequest { get; private set; }
        public (string Message, int Colour) ShoutRequest { get; private set; }
        public (string Parameters, int Colour) WhisperRequest { get; private set; }
        public Task Chat(GameClient session, string message, int colour)
        { ChatRequest = (message, colour); return Task.CompletedTask; }
        public Task Shout(GameClient session, string message, int colour)
        { ShoutRequest = (message, colour); return Task.CompletedTask; }
        public Task Whisper(GameClient session, string parameters, int colour)
        { WhisperRequest = (parameters, colour); return Task.CompletedTask; }
    }

    private sealed class RecordingLogs : IChatlogManager
    {
        public List<ChatlogEntry> Entries { get; } = [];
        public void StoreChatlog(ChatlogEntry entry) => Entries.Add(entry);
        public void FlushAndSave() { }
    }

    private sealed class RecordingCommands : ICommandManager
    {
        public bool Handled { get; set; }
        public List<string> Messages { get; } = [];
        public Task<bool> Parse(GameClient session, string message)
        { Messages.Add(message); return Task.FromResult(Handled); }
        public void Register(string commandText, ICommandBase command) => throw new NotSupportedException();
        public void LogCommand(int userId, string data, string machineId) => throw new NotSupportedException();
        public bool TryGetCommand(string command, out ICommandBase? chatCommand)
        { chatCommand = null; return false; }
    }

    private sealed class RecordingFilter : IWordFilterManager
    {
        public bool Banned { get; set; }
        public string Replacement { get; set; } = "";
        public void Init() { }
        public string CheckMessage(string message) => Replacement.Length == 0 ? message : Replacement;
        public bool CheckBannedWords(string message) => Banned;
        public bool IsFiltered(string message) => Banned;
    }

    private sealed class Styles : IChatStyleManager
    {
        public void Init() { }
        public IReadOnlyList<int> GetAllowedStyleIds(Plus.HabboHotel.Permissions.UserAccess access) => [0, 1, 2, 3];
        public bool TryGetStyle(int id, out ChatStyle? style)
        { style = new(id, "test", ""); return true; }
    }

    private sealed class Settings : ISettingsManager
    {
        public int Chances { get; set; } = 99;
        public string TryGetValue(string value) => Chances.ToString();
        public string? GetOptionalValue(string key) => null;
        public Task Reload() => Task.CompletedTask;
    }

    private sealed class RecordingQuests : IQuestManager
    {
        public List<QuestType> Progresses { get; } = [];
        public void ProgressUserQuest(GameClient session, QuestType type, int data = 0) => Progresses.Add(type);
        public void Init() => throw new NotSupportedException();
        public Quest GetQuest(int id) => throw new NotSupportedException();
        public int GetAmountOfQuestsInCategory(string category) => throw new NotSupportedException();
        public Quest GetNextQuestInSeries(string category, int number) => throw new NotSupportedException();
        public void GetList(GameClient session, Plus.Communication.Packets.Incoming.ClientPacket message) => throw new NotSupportedException();
        public void QuestReminder(GameClient session, int questId) => throw new NotSupportedException();
    }

    private sealed class RecordingRewards : IRewardTrackManager
    {
        public List<string> Progresses { get; } = [];
        public void Progress(GameClient session, string actionType, int amount = 1) => Progresses.Add(actionType);
        public void SendTracks(GameClient session) => throw new NotSupportedException();
        public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
        public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ZonedClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public int Calls { get; set; }
        public override DateTimeOffset GetUtcNow()
        { Calls++; return now; }
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    public class ClientDirectory : DispatchProxy
    {
        public Dictionary<int, GameClient> ByUserId { get; } = [];
        public static IGameClientManager Create(out ClientDirectory directory)
        {
            var manager = DispatchProxy.Create<IGameClientManager, ClientDirectory>();
            directory = (ClientDirectory)(object)manager;
            return manager;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IGameClientManager.GetClientByUserId))
                return ByUserId.GetValueOrDefault((int)args![0]!);
            return targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }

    public class RecordingModeration : DispatchProxy
    {
        public DateTimeOffset? Expiry { get; private set; }
        public static (IModerationManager Manager, RecordingModeration Recorder) Create()
        {
            var manager = DispatchProxy.Create<IModerationManager, RecordingModeration>();
            return (manager, (RecordingModeration)(object)manager);
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IModerationManager.BanUser))
            {
                Expiry = (DateTimeOffset?)args![4];
                return Task.CompletedTask;
            }
            return targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }

    private class DefaultProxy<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, DefaultProxy<T>>();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask :
            targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
    }

    private static object Get(object value, string field) =>
        value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string field, object data) =>
        value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
}
