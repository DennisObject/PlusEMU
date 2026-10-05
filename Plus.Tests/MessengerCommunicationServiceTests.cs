using System.Reflection;
using Plus.Communication.Packets.Incoming.FriendList;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Xunit;

namespace Plus.Tests;

public sealed class MessengerCommunicationServiceTests
{
    [Fact]
    public async Task MissingFriendNotifiesBeforeFilterAndNeverDelivers()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger());

        await f.Service().SendMessage(session, 99, "hello");

        Assert.Equal(new[] { "error NotFriends 99" }, f.Output.Calls);
        Assert.Empty(f.Rewards.Progressed);
        Assert.Empty(f.Delivered);
    }

    [Fact]
    public async Task EmptyAndMutedMessagesStopBeforeDeliveryWithTheirOwnNotice()
    {
        using var f = new MessengerFixture();
        var messenger = f.Messenger(friend: 2);
        var (session, _) = f.Session(messenger);
        f.Filter.Output = "   ";
        await f.Service().SendMessage(session, 2, "x");
        Assert.Empty(f.Output.Calls);

        f.Filter.Output = "hi";
        session.GetHabbo().TimeMuted = 60;
        await f.Service().SendMessage(session, 2, "hi");
        Assert.Equal(new[] { "notice Oops, you're currently muted - you cannot send messages." }, f.Output.Calls);
        Assert.Empty(f.Rewards.Progressed);
        Assert.Empty(f.Delivered);
    }

    [Fact]
    public async Task FirstDeliveryRewardsThenFloodNoticesWithoutAnotherReward()
    {
        using var f = new MessengerFixture();
        var messenger = f.Messenger(friend: 2);
        for (var i = 0; i < 10; i++) Assert.True(messenger.TrySendHabbicon(f.Clock.GetUtcNow()));
        var (session, _) = f.Session(messenger);
        f.Filter.Output = "hi";

        await f.Service().SendMessage(session, 2, "hi");
        Assert.Equal(new[] { "send_messenger_message" }, f.Rewards.Progressed);
        Assert.Equal(new[] { "hi" }, f.Delivered);
        Assert.Empty(f.Output.Calls);

        await f.Service().SendMessage(session, 2, "blocked");
        Assert.Equal(new[] { "send_messenger_message" }, f.Rewards.Progressed);
        Assert.Equal(new[] { "hi" }, f.Delivered);
        Assert.Single(f.Output.Calls);
        Assert.StartsWith("notice You cannot send a message, you have flooded the console.", f.Output.Calls[0]);
    }

    [Fact]
    public async Task BlockedOrUnknownRequestTargetsDoNothingAndUnknownsSkipTheQuest()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger());

        f.Loader.Next = Task.FromResult((5, true));
        await f.Service().RequestFriend(session, "blocked");
        f.Loader.Next = Task.FromResult((0, false));
        await f.Service().RequestFriend(session, "missing");

        Assert.Empty(f.Rewards.Progressed);
        Assert.Empty(f.Quests.Calls);
        Assert.False(session.GetHabbo().Messenger.Requests.ContainsKey(5));
    }

    [Fact]
    public async Task NewRequestRewardsOnceAndQuestsAfterTheAttempt()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger());
        f.Loader.Next = Task.FromResult((5, false));

        await f.Service().RequestFriend(session, "friend");

        Assert.Equal(new[] { "request_friend", "quest SocialFriend" }, f.Order);
    }

    [Fact]
    public async Task AcceptingAnExistingRequestSkipsTheRewardButStillProgressesTheQuest()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger(requestFrom: 5));
        f.Loader.Next = Task.FromResult((5, false));

        await f.Service().RequestFriend(session, "friend");

        Assert.Empty(f.Rewards.Progressed);
        Assert.Equal(new[] { "quest SocialFriend" }, f.Quests.Calls);
    }

    [Fact]
    public async Task RefusedRequestSkipsTheRewardButStillProgressesTheQuest()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger(outstanding: 5));
        f.Loader.Next = Task.FromResult((5, false));

        await f.Service().RequestFriend(session, "friend");

        Assert.Empty(f.Rewards.Progressed);
        Assert.Equal(new[] { "quest SocialFriend" }, f.Quests.Calls);
    }

    [Fact]
    public async Task RequestWaitsForTheLoaderBeforeAnyRewardOrQuest()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger());
        var pending = new TaskCompletionSource<(int, bool)>();
        f.Loader.Next = pending.Task;

        var request = f.Service().RequestFriend(session, "friend");
        Assert.False(request.IsCompleted);
        Assert.Empty(f.Order);

        pending.SetResult((5, false));
        await request;
        Assert.Equal(new[] { "request_friend", "quest SocialFriend" }, f.Order);
    }

    [Fact]
    public async Task LoaderFailureRejectsTheRequestWithoutRewardOrQuest()
    {
        using var f = new MessengerFixture();
        var (session, _) = f.Session(f.Messenger());
        f.Loader.Next = Task.FromException<(int, bool)>(new InvalidOperationException("Injected loader failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().RequestFriend(session, "friend"));

        Assert.Empty(f.Order);
    }

    [Fact]
    public async Task SuccessfulDeliveryRaisesTheMessengerEventOnce()
    {
        using var f = new MessengerFixture();
        var messenger = f.Messenger(friend: 2);
        var sent = new List<(int Friend, string Text)>();
        messenger.MessageSend += (_, args) => sent.Add((args.Friend.Id, args.Message));
        var (session, _) = f.Session(messenger);
        f.Filter.Output = "hello";

        await f.Service().SendMessage(session, 2, "hello");

        Assert.Equal(new[] { (2, "hello") }, sent);
        Assert.Equal(new[] { "send_messenger_message" }, f.Rewards.Progressed);
    }

    [Fact]
    public async Task SendMsgHandlerDecodesFriendAndTextBeforeDelegating()
    {
        var service = new RecordingCommunication();
        var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        session.SetHabbo(new Habbo { Id = 1, Username = "Alice" });

        await new SendMsgEvent(service).Parse(session, Packet(7, "hi"));
        Assert.Equal(new[] { "send 7 hi" }, service.Calls);

        service.Calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new SendMsgEvent(service).Parse(session, Packet(7)));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task RequestHandlerDecodesTheNameBeforeDelegating()
    {
        var service = new RecordingCommunication();
        var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        session.SetHabbo(new Habbo { Id = 1, Username = "Alice" });

        await new RequestFriendEvent(service).Parse(session, Packet("Bob"));
        Assert.Equal(new[] { "request Bob" }, service.Calls);
    }

    private static IIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is int number)
            {
                var bytes = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes((string)value);
                var length = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
                stream.Write(length);
                stream.Write(bytes);
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private sealed class RecordingCommunication : IMessengerCommunicationService
    {
        public List<string> Calls { get; } = [];
        public Task SendMessage(GameClient session, int friendId, string text) { Calls.Add($"send {friendId} {text}"); return Task.CompletedTask; }
        public Task RequestFriend(GameClient session, string username) { Calls.Add($"request {username}"); return Task.CompletedTask; }
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (method, _) => throw new NotSupportedException(method.Name);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private sealed class RecordingOutput : IMessengerCommunicationOutput
    {
        public List<string> Calls { get; } = [];
        public void InstantMessageError(GameClient session, MessengerMessageErrors error, int userId) => Calls.Add($"error {error} {userId}");
        public void Notice(GameClient session, string text) => Calls.Add("notice " + text);
    }

    private sealed class FakeLoader
    {
        public Task<(int userId, bool blockFriendRequests)> Next { get; set; } = Task.FromResult((0, false));
    }

    private sealed class MessengerFixture : IDisposable
    {
        public readonly ManualClock Clock = new();
        public readonly FilterStub Filter = new();
        public readonly RecordingRewards Rewards = new();
        public readonly RecordingQuests Quests = new();
        public readonly RecordingOutput Output = new();
        public readonly FakeLoader Loader = new();
        public readonly List<string> Order = [];
        public readonly List<string> Delivered = [];

        public MessengerFixture()
        {
            Rewards.Order = Order; Quests.Order = Order;
        }

        public HabboMessenger Messenger(int? friend = null, int? requestFrom = null, int? outstanding = null)
        {
            var friends = new Dictionary<int, MessengerBuddy>();
            if (friend is { } friendId) friends[friendId] = new MessengerBuddy { Id = friendId };
            var requests = new Dictionary<int, MessengerRequest>();
            if (requestFrom is { } from) requests[from] = new MessengerRequest { FromId = from, ToId = 1 };
            var pending = new List<int>();
            if (outstanding is { } pendingId) pending.Add(pendingId);
            var messenger = new HabboMessenger(friends, requests, pending, Clock);
            messenger.MessageSend += (_, args) => Delivered.Add(args.Message);
            return messenger;
        }

        public (FlashGameClient Session, object Unused) Session(HabboMessenger messenger)
        {
            var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
            session.SetHabbo(new Habbo { Id = 1, Username = "Alice", Messenger = messenger });
            return (session, 0);
        }

        public MessengerCommunicationService Service()
        {
            var loader = DispatchProxy.Create<IMessengerDataLoader, ServiceProxy>();
            ((ServiceProxy)(object)loader).Handler = (method, _) => method.Name == nameof(IMessengerDataLoader.CanReceiveFriendRequests)
                ? Loader.Next : throw new NotSupportedException(method.Name);
            return new MessengerCommunicationService(Filter, (IMessengerDataLoader)(object)loader, Quests, Rewards, Output);
        }

        public void Dispose() { }
    }

    private sealed class FilterStub : IWordFilterManager
    {
        public string Output { get; set; } = "";
        public void Init() { }
        public string CheckMessage(string message) => Output;
        public bool CheckBannedWords(string message) => false;
        public bool IsFiltered(string message) => false;
    }

    private sealed class RecordingRewards : IRewardTrackManager
    {
        public List<string> Progressed { get; } = [];
        public List<string>? Order { get; set; }
        public void Progress(GameClient session, string actionType, int amount = 1) { Progressed.Add(actionType); Order?.Add(actionType); }
        public void SendTracks(GameClient session) => throw new NotSupportedException();
        public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
        public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
    }

    private sealed class RecordingQuests : IQuestManager
    {
        public List<string> Calls { get; } = [];
        public List<string>? Order { get; set; }
        public void ProgressUserQuest(GameClient session, QuestType type, int data = 0) { Calls.Add("quest " + type); Order?.Add("quest " + type); }
        public void Init() => throw new NotSupportedException();
        public Quest? GetQuest(int id) => throw new NotSupportedException();
        public int GetAmountOfQuestsInCategory(string category) => throw new NotSupportedException();
        public Quest? GetNextQuestInSeries(string category, int number) => throw new NotSupportedException();
        public void GetList(GameClient session, Plus.Communication.Packets.Incoming.ClientPacket message) => throw new NotSupportedException();
        public void QuestReminder(GameClient session, int questId) => throw new NotSupportedException();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
