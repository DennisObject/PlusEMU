using System.Buffers.Binary;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

/// <summary>A new outgoing request through the real communication, mutation, messenger and synchronizer code; only the loader's commit point is faked.</summary>
public sealed class MessengerRequestSendTests
{
    [Fact]
    public async Task NewOutgoingRequestPublishesOnceAfterTheCommitAndRewardsOnce()
    {
        var clients = new GameClientManager(null!, null!);
        var order = new List<string>();
        var registrations = 0;
        (int Outstanding, int Events, int Packets, int Requests)? atCommit = null;
        var sentEvents = 0;

        var sender = new Participant(1, "Alice", new HabboMessenger([], [], [], TimeProvider.System));
        var target = new Participant(2, "Bob", new HabboMessenger([], [], [], TimeProvider.System));
        sender.Messenger.FriendRequestUpdated += (_, args) => { if (args.FriendRequestModificationType == FriendRequestModificationType.Sent) sentEvents++; };
        clients.RegisterClient(target.Client, target.Habbo.Id, target.Habbo.Username);

        // The commit point: the store is written, nothing in memory or on the wire has changed yet.
        var loader = Fake<IMessengerDataLoader>((method, _) => method.Name switch
        {
            nameof(IMessengerDataLoader.CanReceiveFriendRequests) => Task.FromResult((2, false)),
            nameof(IMessengerDataLoader.RegisterFriendRequest) => Commit(),
            _ => throw new NotSupportedException(method.Name),
        });
        Task<bool> Commit()
        {
            registrations++;
            atCommit = (sender.Messenger.OutstandingFriendRequests.Count, sentEvents, target.Headers.Count, target.Messenger.Requests.Count);
            return Task.FromResult(true);
        }

        var synchronizer = new MessengerEventSynchronizer(loader, clients);
        await synchronizer.UserLoggedIn(sender.Habbo);
        await synchronizer.UserLoggedIn(target.Habbo);
        var quests = Fake<IQuestManager>((method, args) => Record(order, method, args));
        var rewards = Fake<IRewardTrackManager>((method, args) => Record(order, method, args));
        var communication = new MessengerCommunicationService(
            Fake<IWordFilterManager>((_, _) => throw new NotSupportedException()),
            loader,
            quests,
            rewards,
            Fake<IMessengerCommunicationOutput>((_, _) => throw new NotSupportedException()),
            new MessengerFriendMutationService(loader, new AccountSessionGate(), clients));

        await communication.RequestFriend(sender.Client, "Bob");

        Assert.Equal((0, 0, 0, 0), atCommit);
        Assert.Equal(1, registrations);
        Assert.Equal(new[] { target.Habbo.Id }, sender.Messenger.OutstandingFriendRequests);
        Assert.Equal(1, sentEvents);
        var received = Assert.Single(target.Messenger.Requests.Values);
        Assert.Equal((sender.Habbo.Id, sender.Habbo.Username), (received.FromId, received.Username));
        Assert.Equal(new uint[] { ServerPacketHeader.NewBuddyRequestComposer }, target.Headers);
        Assert.Equal(new[] { $"Progress {RewardTrackActions.RequestFriend}", "ProgressUserQuest SocialFriend" }, order);

        // A repeat is refused from memory: no second store write, event, notification, or reward; the quest still counts the attempt.
        await communication.RequestFriend(sender.Client, "Bob");

        Assert.Equal(1, registrations);
        Assert.Equal(new[] { target.Habbo.Id }, sender.Messenger.OutstandingFriendRequests);
        Assert.Equal(1, sentEvents);
        Assert.Equal(new uint[] { ServerPacketHeader.NewBuddyRequestComposer }, target.Headers);
        Assert.Equal(new[] { $"Progress {RewardTrackActions.RequestFriend}", "ProgressUserQuest SocialFriend", "ProgressUserQuest SocialFriend" }, order);
    }

    private static object? Record(List<string> order, MethodInfo method, object?[]? args)
    {
        order.Add($"{method.Name} {args![1]}");
        return null;
    }

    private static T Fake<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MessengerCommunicationServiceTests.ServiceProxy>();
        ((MessengerCommunicationServiceTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class Participant
    {
        public Participant(int id, string username, HabboMessenger messenger)
        {
            Headers = [];
            Client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new Revision { InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.FieldType == typeof(uint)).Select(field => (uint)field.GetValue(null)!).Where(headerId => headerId > 0).Distinct().ToDictionary(headerId => headerId, headerId => headerId) },
                SendCallback = args =>
                {
                    var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
                    Headers.Add(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)));
                    return true;
                }
            };
            Habbo = new Habbo { Id = id, Username = username, Messenger = messenger, Client = Client };
            Client.SetHabbo(Habbo);
            Messenger = messenger;
        }

        public FlashGameClient Client { get; }
        public Habbo Habbo { get; }
        public HabboMessenger Messenger { get; }
        public List<uint> Headers { get; }
    }
}
