using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class GroupAppearanceServiceTests
{
    [Fact]
    public async Task HandlersDecodePrimitiveRequestsAndRejectMalformedBadges()
    {
        var appearance = new RecordingAppearanceService();

        await new UpdateGroupIdentityEvent(appearance).Parse(null!, HabbiconTestSupport.Incoming(7, "name", "description"));
        await new UpdateGroupBadgeEvent(appearance).Parse(null!, HabbiconTestSupport.Incoming(8, 6, 1, 2, 3, 4, 5, 6));
        await new UpdateGroupColoursEvent(appearance).Parse(null!, HabbiconTestSupport.Incoming(9, 10, 11));
        await new UpdateGroupBadgeEvent(appearance).Parse(null!, HabbiconTestSupport.Incoming(8, 4, 1, 2, 3, 4));

        Assert.Equal(new(7, "name", "description"), appearance.Identity);
        Assert.Equal(8, appearance.BadgeGroupId);
        Assert.Equal(new[] { new GroupBadgePartRequest(1, 2, 3), new(4, 5, 6) }, appearance.BadgeParts.ToArray());
        Assert.Equal(new(9, 10, 11), appearance.Colours);
        Assert.Equal(1, appearance.BadgeCalls);
    }

    [Fact]
    public async Task IdentityFilteringPrecedesGroupValidation()
    {
        var order = new List<string>();
        var service = new GroupAppearanceService(
            Proxy<IGroupManager>((method, args) => { order.Add("lookup"); args[1] = null; return false; }),
            new RecordingFilter(value => { order.Add("filter:" + value); return "filtered-" + value; }),
            Proxy<IGroupInfoSnapshotService>((method, _) => throw new NotSupportedException(method)),
            new RecordingStore());
        var (client, sent) = Client(7);

        await service.UpdateIdentity(client, new(9, "name", "description"));

        Assert.Equal(["filter:name", "filter:description", "lookup"], order);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task UnauthorizedAndFailedWritesPublishNothing()
    {
        var denied = Group();
        var deniedStore = new RecordingStore();
        var (stranger, strangerSent) = Client(8);
        await Service(denied, deniedStore).UpdateBadge(stranger, denied.Id, [new(1, 1, 4)]);
        Assert.Equal(0, deniedStore.Writes);
        Assert.Equal("b05114s06114", denied.Badge);
        Assert.Empty(strangerSent);

        var failed = Group();
        var failedStore = new RecordingStore { Succeeds = false };
        var (owner, ownerSent) = Client(7);
        await Service(failed, failedStore).UpdateIdentity(owner, new(failed.Id, "new", "new description"));
        Assert.Equal("Crew", failed.Name);
        Assert.Equal("description", failed.Description);
        Assert.Empty(ownerSent);
    }

    [Fact]
    public async Task ColoursPersistBeforeStateAndPublishOnlyMatchingGuildFurniture()
    {
        var group = Group();
        var room = Room();
        var (owner, sent) = Client(7, room);
        AddViewer(room, owner);
        AddItem(room, 10, group.Id, InteractionType.GuildItem);
        AddItem(room, 11, group.Id, InteractionType.GuildForum);
        AddItem(room, 12, group.Id + 1, InteractionType.GuildGate);
        AddItem(room, 13, group.Id, InteractionType.Gate);
        var store = new RecordingStore(() =>
        {
            Assert.Equal(3, group.Colour1);
            Assert.Equal(4, group.Colour2);
            Assert.Empty(sent);
        });
        var snapshots = new RecordingGroupInfo();
        var service = Service(group, store, snapshots);

        await service.UpdateColours(owner, new(group.Id, 20, 21));

        Assert.Equal(20, group.Colour1);
        Assert.Equal(21, group.Colour2);
        Assert.Equal((20, 21), store.Colours);
        Assert.Equal((20, 21), snapshots.Colours);
        Assert.Equal(ServerPacketHeader.GroupInfoComposer, sent[0].Header);
        var updates = sent.Where(packet => packet.Header == ServerPacketHeader.ObjectUpdateComposer).ToArray();
        Assert.Equal(new uint[] { 10, 11 }, updates.Select(packet => BinaryPrimitives.ReadUInt32BigEndian(packet.Payload)).Order().ToArray());
    }

    [Fact]
    public async Task BadgeUsesBoundedTripletsAndPublishesPreparedGroupInfo()
    {
        var group = Group();
        var store = new RecordingStore();
        var snapshots = new RecordingGroupInfo();
        var (owner, sent) = Client(7);
        var service = Service(group, store, snapshots);

        await service.UpdateBadge(owner, group.Id, [new(1, 1, 4), new(2, 2, 4)]);
        await service.UpdateBadge(owner, group.Id, ImmutableArray<GroupBadgePartRequest>.Empty);

        Assert.Equal("b01014s02024", group.Badge);
        Assert.Equal("b01014s02024", store.Badge);
        Assert.Equal(group.Badge, snapshots.Last!.Badge);
        Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.GroupInfoComposer, sent[0].Header);
    }

    [RoomComponentDatabaseFact]
    public void StoreUpdatesExactRowAndRejectsMissingGroup()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_ga_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);
            using (var connection = database.Connection())
                connection.Execute("CREATE TABLE `groups` (`id` INT PRIMARY KEY,`name` VARCHAR(50) NOT NULL,`desc` VARCHAR(100) NOT NULL,`badge` VARCHAR(50) NOT NULL,`colour1` INT NOT NULL,`colour2` INT NOT NULL); INSERT INTO `groups` VALUES(9,'old','old description','oldbadge',1,2)");
            var store = new GroupAppearanceStore(database);

            Assert.True(store.UpdateIdentity(9, "new", "new description"));
            Assert.True(store.UpdateBadge(9, "b01014s02024"));
            Assert.True(store.UpdateColours(9, 20, 21));
            Assert.False(store.UpdateIdentity(10, "missing", "missing"));

            using var verify = database.Connection();
            Assert.Equal(("new", "new description", "b01014s02024", 20, 21),
                verify.QuerySingle<(string, string, string, int, int)>("SELECT `name`,`desc`,`badge`,`colour1`,`colour2` FROM `groups` WHERE `id`=9"));
            Assert.Equal(1, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM `groups`"));
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static GroupAppearanceService Service(Group group, RecordingStore store, RecordingGroupInfo? info = null) => new(
        Proxy<IGroupManager>((method, args) => { args[1] = group; return true; }),
        new RecordingFilter(value => value),
        info ?? new RecordingGroupInfo(),
        store);

    private static Group Group() => new(9, "Crew", "description", "b05114s06114", 42, 7,
        DateTimeOffset.UnixEpoch, 0, 3, 4, 0, false, new([], [], []));

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(int id, Room? room = null) =>
        HabbiconTestSupport.Client(new Habbo { Id = id, Username = "User" + id, CurrentRoom = room });

    private static Room Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        return room;
    }

    private static void AddViewer(Room room, GameClient client)
    {
        var user = new RoomUser(client.GetHabbo().Id, room.Id, 1, room, client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
        users[user.InternalRoomId] = user;
    }

    private static void AddItem(Room room, uint id, int groupId, InteractionType type)
    {
        var item = new Item
        {
            Id = id,
            RoomId = room.Id,
            GroupId = groupId,
            OwnerId = 7,
            UserId = 7,
            Username = "User7",
            Definition = new ItemDefinition { Id = id, SpriteId = (int)id, Type = ItemType.Floor, InteractionType = type },
            ExtraData = FurniObjectData.Empty,
        };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
            .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        floor[id] = item;
    }

    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!.Name, args!);
    }

    private sealed class RecordingAppearanceService : IGroupAppearanceService
    {
        public GroupIdentityRequest? Identity { get; private set; }
        public int BadgeGroupId { get; private set; }
        public ImmutableArray<GroupBadgePartRequest> BadgeParts { get; private set; }
        public int BadgeCalls { get; private set; }
        public GroupColoursRequest? Colours { get; private set; }
        public Task UpdateIdentity(GameClient session, GroupIdentityRequest request) { Identity = request; return Task.CompletedTask; }
        public Task UpdateBadge(GameClient session, int groupId, ImmutableArray<GroupBadgePartRequest> parts) { BadgeGroupId = groupId; BadgeParts = parts; BadgeCalls++; return Task.CompletedTask; }
        public Task UpdateColours(GameClient session, GroupColoursRequest request) { Colours = request; return Task.CompletedTask; }
    }

    private sealed class RecordingStore(Action? beforeWrite = null) : IGroupAppearanceStore
    {
        public bool Succeeds { get; init; } = true;
        public int Writes { get; private set; }
        public string? Badge { get; private set; }
        public (int Main, int Secondary)? Colours { get; private set; }
        public bool UpdateIdentity(int groupId, string name, string description) { beforeWrite?.Invoke(); Writes++; return Succeeds; }
        public bool UpdateBadge(int groupId, string badge) { beforeWrite?.Invoke(); Writes++; Badge = badge; return Succeeds; }
        public bool UpdateColours(int groupId, int mainColour, int secondaryColour) { beforeWrite?.Invoke(); Writes++; Colours = (mainColour, secondaryColour); return Succeeds; }
    }

    private sealed class RecordingGroupInfo : IGroupInfoSnapshotService
    {
        public GroupInfoSnapshot? Last { get; private set; }
        public (int Main, int Secondary)? Colours { get; private set; }
        public GroupInfoSnapshot Capture(Group group, int viewerId)
        {
            Colours = (group.Colour1, group.Colour2);
            return Last = new(group.Id, group.Type, group.Name, group.Description, group.Badge, group.RoomId,
                "Room", group.MemberCount, "1-1-1970", "Owner", true, false, true, false, 0, true, false);
        }
    }

    private sealed class RecordingFilter(Func<string, string> filter) : IWordFilterManager
    {
        public string CheckMessage(string message) => filter(message);
        public void Init() { }
        public bool CheckBannedWords(string message) => false;
        public bool IsFiltered(string message) => false;
    }

    private sealed class ProbeDatabase(string connectionString) : Plus.Database.IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
