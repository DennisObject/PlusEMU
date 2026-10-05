using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class GroupParticipationTests
{
    [Fact]
    public async Task JoinEventDecodesOneGroupIdAndDelegates()
    {
        var service = new RecordingParticipation();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 8 });

        await new JoinGroupEvent(service).Parse(client, HabbiconTestSupport.Incoming(9));

        Assert.Equal(new object[] { 9 }, Assert.Single(service.Joins));
    }

    [Fact]
    public async Task SetFavouriteEventDecodesOneGroupIdAndDelegates()
    {
        var service = new RecordingParticipation();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 8 });

        await new SetGroupFavouriteEvent(service).Parse(client, HabbiconTestSupport.Incoming(9));

        Assert.Equal(new object[] { 9 }, Assert.Single(service.Favourites));
    }

    [Fact]
    public async Task RemoveFavouriteEventReadsNoInputAndDelegates()
    {
        var service = new RecordingParticipation();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 8 });

        await new RemoveGroupFavouriteEvent(service).Parse(client, HabbiconTestSupport.Incoming(1, 2));

        Assert.Equal(1, service.RemoveCount);
    }

    [Fact]
    public async Task JoinWaitingOnADeletedGroupLockPublishesNothing()
    {
        var group = NewGroup();
        var lookups = 0;
        var manager = CatalogSnapshotTestSupport.Proxy<IGroupManager>((method, args) => method switch
        {
            // The group is found before the lock and is gone once the lock is held.
            "TryGetGroup" when ++lookups == 1 => SetOut(args, group),
            "TryGetGroup" => SetOut(args, null),
            "GetGroupsForUser" => new List<Group>(),
            _ => throw new NotSupportedException(method),
        });
        var store = new FakeParticipationStore();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await new GroupParticipationService(manager, Snapshots(), Clients(new()), store).Join(client, group.Id);

        Assert.Empty(store.Joins);
        Assert.False(group.IsMember(8));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task DuplicateMemberJoinIsANoOpWithoutStoreOrPackets()
    {
        var group = NewGroup();
        group.PublishJoin(8);
        var store = new FakeParticipationStore();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await Service(store, group).Join(client, group.Id);

        Assert.Empty(store.Joins);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task LocalLimitSendsOnlyTheNoticeAndNeverReachesTheStore()
    {
        var group = NewGroup();
        var store = new FakeParticipationStore();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await Service(store, group, memberships: Enumerable.Repeat(group, 1500).ToList()).Join(client, group.Id);

        Assert.Empty(store.Joins);
        Assert.Equal(new[] { ServerPacketHeader.BroadcastMessageAlertComposer }, sent.Select(packet => packet.Header));
        Assert.False(group.IsMember(8));
    }

    [Fact]
    public async Task StoreLimitSendsOnlyTheNoticeAndPublishesNothing()
    {
        var group = NewGroup();
        var store = new FakeParticipationStore { OnJoin = (_, _, _, _) => GroupJoinOutcome.LimitReached };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await Service(store, group).Join(client, group.Id);

        Assert.Equal(new[] { ServerPacketHeader.BroadcastMessageAlertComposer }, sent.Select(packet => packet.Header));
        Assert.False(group.IsMember(8));
    }

    [Fact]
    public async Task LockedRequestPersistsBeforePublishingAndNotifiesAdminsFirst()
    {
        var group = NewGroup();
        group.Type = GroupType.Locked;
        group.MakeAdmin(5);
        var sawRequestAtCommit = true;
        var store = new FakeParticipationStore
        {
            OnJoin = (userId, _, request, limit) =>
            {
                sawRequestAtCommit = group.HasRequest(userId);
                Assert.True(request);
                Assert.Equal(1500, limit);
                return GroupJoinOutcome.Inserted;
            },
        };
        var (admin, adminSent) = HabbiconTestSupport.Client(new Habbo { Id = 5, Username = "Admin" });
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });
        var clients = new List<GameClient> { admin, client };

        await Service(store, group, clients: clients).Join(client, group.Id);

        Assert.False(sawRequestAtCommit);
        Assert.True(group.HasRequest(8));
        Assert.Equal(new[] { ServerPacketHeader.GroupMembershipRequestedComposer }, adminSent.Select(packet => packet.Header));
        Assert.Equal(new[] { ServerPacketHeader.GroupInfoComposer }, sent.Select(packet => packet.Header));
    }

    [Fact]
    public async Task OpenJoinSendsFurniConfigThenInfoThenRefreshInOrder()
    {
        var group = NewGroup();
        var store = new FakeParticipationStore();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await Service(store, group).Join(client, group.Id);

        Assert.True(group.IsMember(8));
        Assert.Equal(new[]
        {
            ServerPacketHeader.GroupFurniConfigComposer,
            ServerPacketHeader.GroupInfoComposer,
            ServerPacketHeader.RefreshFavouriteGroupComposer,
        }, sent.Select(packet => packet.Header));
    }

    [Fact]
    public async Task RefusedJoinWritesNothingAndPublishesNothing()
    {
        var group = NewGroup();
        var store = new FakeParticipationStore { OnJoin = (_, _, _, _) => GroupJoinOutcome.Refused };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await Service(store, group).Join(client, group.Id);

        Assert.False(group.IsMember(8));
        Assert.False(group.HasRequest(8));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task JoinIsPersistedBeforeMemoryIsPublished()
    {
        var group = NewGroup();
        var observedMemberBeforeCommit = true;
        var store = new FakeParticipationStore
        {
            OnJoin = (userId, _, _, _) =>
            {
                observedMemberBeforeCommit = group.IsMember(userId);
                return GroupJoinOutcome.Inserted;
            },
        };
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1" });

        await Service(store, group).Join(client, group.Id);

        Assert.False(observedMemberBeforeCommit);
        Assert.True(group.IsMember(8));
    }

    [Fact]
    public async Task SetFavouritePersistsBeforeMemoryAndSendsRefreshWithoutRoom()
    {
        var group = NewGroup();
        var habbo = new Habbo { Id = 8, Username = "Bob", HabboStats = Stats() };
        var observedBeforeWrite = -1;
        var store = new FakeParticipationStore
        {
            OnFavourite = (userId, groupId) =>
            {
                observedBeforeWrite = habbo.HabboStats.FavouriteGroupId;
                return true;
            },
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Service(store, group).SetFavourite(client, group.Id);

        Assert.Equal(new object[] { 8, group.Id }, Assert.Single(store.Favourites));
        Assert.Equal(0, observedBeforeWrite);
        Assert.Equal(group.Id, habbo.HabboStats.FavouriteGroupId);
        Assert.Equal(new[] { ServerPacketHeader.RefreshFavouriteGroupComposer }, sent.Select(packet => packet.Header));
    }

    [Fact]
    public async Task FailedFavouriteWriteKeepsMemoryAndSendsNothing()
    {
        var group = NewGroup();
        var habbo = new Habbo { Id = 8, Username = "Bob", HabboStats = Stats() };
        var store = new FakeParticipationStore { OnFavourite = (_, _) => false };
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Service(store, group).SetFavourite(client, group.Id);

        Assert.Equal(0, habbo.HabboStats.FavouriteGroupId);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ZeroOrUnknownFavouriteGroupDoesNothing()
    {
        var group = NewGroup();
        var store = new FakeParticipationStore();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, HabboStats = Stats() });

        await Service(store, group).SetFavourite(client, 0);
        await Service(store, group).SetFavourite(client, 77);

        Assert.Empty(store.Favourites);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task RemoveFavouritePersistsZeroBeforeMemoryAndSendsRefreshWithoutRoom()
    {
        var group = NewGroup();
        var habbo = new Habbo { Id = 8, Username = "Bob", HabboStats = Stats(group.Id) };
        var observedBeforeWrite = -1;
        var store = new FakeParticipationStore
        {
            OnFavourite = (_, groupId) =>
            {
                observedBeforeWrite = habbo.HabboStats.FavouriteGroupId;
                Assert.Equal(0, groupId);
                return true;
            },
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Service(store, group).RemoveFavourite(client);

        Assert.Equal(group.Id, observedBeforeWrite);
        Assert.Equal(0, habbo.HabboStats.FavouriteGroupId);
        Assert.Equal(new[] { ServerPacketHeader.RefreshFavouriteGroupComposer }, sent.Select(packet => packet.Header));
    }

    [Fact]
    public async Task FailedRemoveKeepsTheFavouriteAndSendsNothing()
    {
        var group = NewGroup();
        var habbo = new Habbo { Id = 8, Username = "Bob", HabboStats = Stats(group.Id) };
        var store = new FakeParticipationStore { OnFavourite = (_, _) => false };
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Service(store, group).RemoveFavourite(client);

        Assert.Equal(group.Id, habbo.HabboStats.FavouriteGroupId);
        Assert.Empty(sent);
    }

    private static GroupParticipationService Service(FakeParticipationStore store, Group group, List<Group>? memberships = null, List<GameClient>? clients = null) =>
        new(Manager(group, memberships), Snapshots(), Clients(clients ?? new()), store);

    private static HabboStats Stats(int favourite = 0) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, favourite, "", 0);

    private static Group NewGroup()
    {
        return new Group(9, "Crew", "desc", "b01014s02024", 42, 7,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 0, 3, 4, 0,
            false, GroupMembershipSnapshot.Empty);
    }

    private static IGroupManager Manager(Group group, List<Group>? memberships) =>
        CatalogSnapshotTestSupport.Proxy<IGroupManager>((method, args) => method switch
        {
            "TryGetGroup" when (int)args[0]! == group.Id => SetOut(args, group),
            "TryGetGroup" => SetOut(args, null),
            "GetGroupsForUser" => memberships ?? new List<Group>(),
            "GetColourCode" => "",
            _ => throw new NotSupportedException(method),
        });

    private static bool SetOut(object?[] args, Group? group)
    {
        args[1] = group;
        return group != null;
    }

    private static IGroupInfoSnapshotService Snapshots() =>
        CatalogSnapshotTestSupport.Proxy<IGroupInfoSnapshotService>((method, args) =>
        {
            var group = (Group)args[0]!;
            return new GroupInfoSnapshot(group.Id, group.Type, group.Name, group.Description, group.Badge, group.RoomId, "HQ",
                group.MemberCount, "1-1-2023", "Owner", false, false, false, false, group.RequestCount, false, group.ForumEnabled);
        });

    private static IGameClientManager Clients(List<GameClient> clients) =>
        CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, _) => method switch
        {
            "get_GetClients" => clients,
            _ => throw new NotSupportedException(method),
        });

    private sealed class RecordingParticipation : IGroupParticipationService
    {
        public List<object[]> Joins { get; } = new();
        public List<object[]> Favourites { get; } = new();
        public int RemoveCount { get; private set; }
        public Task Join(GameClient session, int groupId)
        {
            Joins.Add(new object[] { groupId });
            return Task.CompletedTask;
        }
        public Task SetFavourite(GameClient session, int groupId)
        {
            Favourites.Add(new object[] { groupId });
            return Task.CompletedTask;
        }
        public Task RemoveFavourite(GameClient session)
        {
            RemoveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeParticipationStore : IGroupParticipationStore
    {
        public Func<int, int, bool, int, GroupJoinOutcome> OnJoin { get; init; } = (_, _, _, _) => GroupJoinOutcome.Inserted;
        public Func<int, int, bool> OnFavourite { get; init; } = (_, _) => true;
        public List<object[]> Joins { get; } = new();
        public List<object[]> Favourites { get; } = new();
        public GroupJoinOutcome Join(int userId, int groupId, bool request, int membershipLimit)
        {
            Joins.Add(new object[] { userId, groupId, request, membershipLimit });
            return OnJoin(userId, groupId, request, membershipLimit);
        }
        public bool SaveFavourite(int userId, int groupId)
        {
            Favourites.Add(new object[] { userId, groupId });
            return OnFavourite(userId, groupId);
        }
    }
}
