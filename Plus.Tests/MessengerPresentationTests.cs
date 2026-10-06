using System.Buffers.Binary;
using System.Collections.Immutable;
using Plus.Communication.Packets.Incoming.FriendList;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Core.Settings;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public class MessengerPresentationTests
{
    [Fact]
    public void BuddyListWritesEveryFieldInTheExistingOrder()
    {
        var buddy = Buddy(7, "Ada", online: true, allowsFollowing: true, relationship: 2);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new BuddyListComposer([MessengerBuddySnapshot.Capture(buddy)], 1, 0).Compose(packet);

        Assert.Equal(new object[]
        {
            1, 0, 1,
            7, "Ada", 1, true, false, "look-Ada", 0, "motto", "", "", true, false, false, (short)2,
        }, packet.Writes);
    }

    [Fact]
    public void OfflineBuddyWritesOfflineAndNotFollowableFlags()
    {
        var buddy = Buddy(8, "Bo", online: false, allowsFollowing: true, relationship: 0);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new BuddyListComposer([MessengerBuddySnapshot.Capture(buddy)], 1, 0).Compose(packet);

        Assert.Equal(false, packet.Writes[6]);
        Assert.Equal(false, packet.Writes[7]);
    }

    [Fact]
    public void SnapshotIsNotChangedByLaterSourceMutation()
    {
        var buddy = Buddy(9, "Before", online: true, allowsFollowing: true, relationship: 0);
        var snapshots = ImmutableArray.Create(MessengerBuddySnapshot.Capture(buddy));

        buddy.Username = "After";
        buddy.Look = "changed";
        var packet = new HabbiconTestSupport.RecordingPacket();
        new BuddyListComposer(snapshots, 1, 0).Compose(packet);

        Assert.Equal("Before", packet.Writes[4]);
        Assert.Equal("look-Before", packet.Writes[8]);
    }

    [Fact]
    public void RecomposingTheSameSnapshotsProducesIdenticalBytes()
    {
        var snapshots = ImmutableArray.Create(MessengerBuddySnapshot.Capture(Buddy(3, "Cy", online: true, allowsFollowing: false, relationship: 1)));
        var first = new HabbiconTestSupport.RecordingPacket();
        var second = new HabbiconTestSupport.RecordingPacket();

        new BuddyListComposer(snapshots, 1, 0).Compose(first);
        new BuddyListComposer(snapshots, 1, 0).Compose(second);

        Assert.Equal(first.Writes, second.Writes);
    }

    [Fact]
    public void RemovedModificationWritesOnlyTheBuddyId()
    {
        var removed = MessengerBuddyModification.Capture(Buddy(4, "Gone", online: true, allowsFollowing: true, relationship: 0), BuddyModificationType.Removed);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new FriendListUpdateComposer([removed]).Compose(packet);

        Assert.Equal(new object[] { 0, 1, (int)BuddyModificationType.Removed, 4 }, packet.Writes);
    }

    [Fact]
    public void ModificationListIsCopiedAtConstructionSoSourceMutationDoesNotChangeOutput()
    {
        var source = new List<MessengerBuddyModification>
        {
            MessengerBuddyModification.Capture(Buddy(4, "Gone", online: true, allowsFollowing: true, relationship: 0), BuddyModificationType.Removed),
        };
        var composer = new FriendListUpdateComposer(source);
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);

        source.Add(MessengerBuddyModification.Capture(Buddy(5, "Late", online: false, allowsFollowing: true, relationship: 0), BuddyModificationType.Added));
        source[0] = MessengerBuddyModification.Capture(Buddy(6, "Swapped", online: true, allowsFollowing: true, relationship: 0), BuddyModificationType.Removed);
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        var again = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(again);

        Assert.Equal(new object[] { 0, 1, (int)BuddyModificationType.Removed, 4 }, before.Writes);
        Assert.Equal(before.Writes, after.Writes);
        Assert.Equal(before.Writes, again.Writes);
    }

    [Fact]
    public void AddedModificationWritesTypeThenTheBuddyEntry()
    {
        var added = MessengerBuddyModification.Capture(Buddy(5, "New", online: false, allowsFollowing: true, relationship: 0), BuddyModificationType.Added);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new FriendListUpdateComposer([added]).Compose(packet);

        Assert.Equal(new object[]
        {
            0, 1, (int)BuddyModificationType.Added,
            5, "New", 0, false, false, "look-New", 0, "motto", "", "", true, false, false, (short)0,
        }, packet.Writes);
    }

    [Fact]
    public async Task FriendListPagesAtFiveHundredAndSendsOfflineMessagesLast()
    {
        var friends = Enumerable.Range(1, 501).ToDictionary(id => id, id => Buddy(id, $"u{id}", online: true, allowsFollowing: true, relationship: 0));
        var (client, sent) = HabbiconTestSupport.Client(HabboWith(friends, new Dictionary<int, MessengerRequest>()));
        var service = Service(offline: new()
        {
            [2] = [("hi", 5)]
        });

        await service.ShowFriendList(client);

        var headers = sent.Select(packet => packet.Header).ToList();
        Assert.Equal(new uint[]
        {
            ServerPacketHeader.MessengerInitComposer,
            ServerPacketHeader.BuddyListComposer,
            ServerPacketHeader.BuddyListComposer,
            ServerPacketHeader.NewConsoleMessageComposer,
        }, headers);
        var pages = sent.Where(packet => packet.Header == ServerPacketHeader.BuddyListComposer).Select(packet => (Pages: ReadInt(packet.Payload, 0), Page: ReadInt(packet.Payload, 4), Count: ReadInt(packet.Payload, 8))).ToList();
        Assert.Equal(new[] { (2, 0, 500), (2, 1, 1) }, pages);
    }

    [Fact]
    public async Task EmptyFriendListSendsOnePageOfZeroThenOfflineMessages()
    {
        var (client, sent) = HabbiconTestSupport.Client(HabboWith(new Dictionary<int, MessengerBuddy>(), new Dictionary<int, MessengerRequest>()));
        var service = Service(offline: new());

        await service.ShowFriendList(client);

        var list = sent.Single(packet => packet.Header == ServerPacketHeader.BuddyListComposer);
        Assert.Equal((1, 0, 0), (ReadInt(list.Payload, 0), ReadInt(list.Payload, 4), ReadInt(list.Payload, 8)));
        Assert.Equal(new[] { ServerPacketHeader.MessengerInitComposer, ServerPacketHeader.BuddyListComposer }, sent.Select(packet => packet.Header));
    }

    [Fact]
    public void FriendRequestsUseCachedLookOrEmpty()
    {
        var requests = new Dictionary<int, MessengerRequest>
        {
            [11] = new() { FromId = 11, ToId = 1, Username = "Req", Figure = "f" },
            [12] = new() { FromId = 12, ToId = 1, Username = "Nope", Figure = "f" },
        };
        var cache = CatalogSnapshotTestSupport.Proxy<ICacheManager>((method, args) =>
            method == "GenerateUser" && (int)args![0]! == 11 ? new CachedUser { Id = 11, Look = "cached-look" } : null);
        var (client, sent) = HabbiconTestSupport.Client(HabboWith(new Dictionary<int, MessengerBuddy>(), requests));

        new MessengerPresentationService(new RecordingLoader(), cache, Settings).ShowFriendRequests(client);

        var payload = sent.Single().Payload;
        Assert.Equal(ServerPacketHeader.FriendRequestsComposer, sent.Single().Header);
        Assert.Equal(2, ReadInt(payload, 0));
        Assert.Equal(11, ReadInt(payload, 8));
    }

    [Fact]
    public async Task InitEventDelegatesToThePresentationService()
    {
        var presentation = new RecordingPresentation();
        var handler = new MessengerInitEvent(presentation);

        await handler.Parse(null!, null!);

        Assert.Equal(new[] { "list" }, presentation.Calls);
    }

    [Fact]
    public async Task FriendRequestsEventDelegatesToThePresentationService()
    {
        var presentation = new RecordingPresentation();
        var handler = new GetFriendRequestsEvent(presentation);

        await handler.Parse(null!, null!);

        Assert.Equal(new[] { "requests" }, presentation.Calls);
    }

    private static MessengerBuddy Buddy(int id, string name, bool online, bool allowsFollowing, int relationship)
    {
        var buddy = new MessengerBuddy { Id = id, Username = name, Relationship = relationship, Look = $"look-{name}", Motto = "motto" };

        if (online)
        {
            buddy.Habbo = new Habbo { Id = id, Username = name, Look = $"look-{name}", Motto = "motto", Gender = "M", AllowUserFollowing = allowsFollowing };
        }

        return buddy;
    }

    private static Habbo HabboWith(Dictionary<int, MessengerBuddy> friends, Dictionary<int, MessengerRequest> requests) =>
        new()
        {
            Id = 1,
            Username = "Owner",
            Messenger = new HabboMessenger(friends, requests, new List<int>(), new FixedTimeProvider(FixedTimeProvider.Epoch))
        };

    private static MessengerPresentationService Service(Dictionary<int, List<(string, int)>> offline) =>
        new(new RecordingLoader(offline), CatalogSnapshotTestSupport.Proxy<ICacheManager>((_, _) => null), Settings);

    private static ISettingsManager Settings { get; } = CatalogSnapshotTestSupport.Proxy<ISettingsManager>((_, _) => null);

    private static int ReadInt(byte[] payload, int offset) => BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset, 4));

    private sealed class RecordingLoader(Dictionary<int, List<(string, int)>>? offline = null) : IMessengerDataLoader
    {
        public Task<Dictionary<int, List<(string Message, int SecondsAgo)>>> GetAndDeleteOfflineMessages(int userId) =>
            Task.FromResult(offline?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<int, List<(string Message, int SecondsAgo)>>());
        public Task<Dictionary<int, (MessengerBuddy buddy, int count)>> GetRelationshipsForUserAsync(int userId) => throw new NotSupportedException();
        public Task<List<MessengerBuddy>> GetBuddiesForUser(int userId) => throw new NotSupportedException();
        public Task<List<MessengerRequest>> GetRequestsForUser(int userId) => throw new NotSupportedException();
        public Task<List<int>> GetOutstandingRequestsForUser(int userId) => throw new NotSupportedException();
        public Task<FriendAcceptResult> AcceptFriendRequest(int acceptorId, int fromId) => throw new NotSupportedException();
        public Task<MessengerBuddy> CreateBuddy(int userId) => throw new NotSupportedException();
        public Task<MessengerBuddy?> GetBuddy(int userId, int friendId) => throw new NotSupportedException();
        public void BroadcastStatusUpdate(Habbo habbo, MessengerEventTypes eventType, string value) => throw new NotSupportedException();
        public Task LogPrivateMessage(int fromId, int toId, string message) => throw new NotSupportedException();
        public Task LogPrivateOfflineMessage(int fromId, int toId, string message) => throw new NotSupportedException();
        public Task<int> DeleteFriendship(int userOneId, int userTwoId) => throw new NotSupportedException();
        public Task SetRelationship(int userOneId, int userTwoId, int relationship) => throw new NotSupportedException();
        public Task<int> DeleteFriendRequest(int fromUserId, int toUserId) => throw new NotSupportedException();
        public Task<bool> RegisterFriendRequest(int fromUserId, int toUserId) => throw new NotSupportedException();
        public Task<(int userId, bool blockFriendRequests)> CanReceiveFriendRequests(string name) => throw new NotSupportedException();
        public Task<int> GetFriendCount(int userId) => throw new NotSupportedException();
    }

    private sealed class RecordingPresentation : IMessengerPresentationService
    {
        public List<string> Calls { get; } = new();
        public Task ShowFriendList(Plus.HabboHotel.GameClients.GameClient session)
        {
            Calls.Add("list");

            return Task.CompletedTask;
        }
        public void ShowFriendRequests(Plus.HabboHotel.GameClients.GameClient session) => Calls.Add("requests");
    }
}
