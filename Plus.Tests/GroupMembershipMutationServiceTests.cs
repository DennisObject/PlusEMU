using System.Data;
using System.Collections.Immutable;
using System.Reflection;
using System.Buffers.Binary;
using System.Text;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GroupMembershipMutationServiceTests
{
    [Fact]
    public async Task HandlersDecodeGroupAndUserIdsThenDelegate()
    {
        var mutations = new RecordingService();

        await new AcceptGroupMembershipEvent(mutations).Parse(null!, HabbiconTestSupport.Incoming(7, 8));
        await new DeclineGroupMembershipEvent(mutations).Parse(null!, HabbiconTestSupport.Incoming(8, 9));
        await new GiveAdminRightsEvent(mutations).Parse(null!, HabbiconTestSupport.Incoming(9, 10));
        await new TakeAdminRightsEvent(mutations).Parse(null!, HabbiconTestSupport.Incoming(11, 12));

        Assert.Equal(new[] { ("accept", 7, 8), ("decline", 8, 9), ("give", 9, 10), ("take", 11, 12) }, mutations.Calls);
    }

    [Fact]
    public async Task SettingsHandlerDecodesPrimitiveRequestAndDelegates()
    {
        var settings = new RecordingSettingsService();

        await new UpdateGroupSettingsEvent(settings).Parse(null!, HabbiconTestSupport.Incoming(9, 2, 1, true));

        Assert.Equal(new(9, 2, 1, true), settings.Request);
    }

    [Theory]
    [InlineData(7, false, false)]
    [InlineData(4, true, false)]
    [InlineData(5, false, true)]
    public async Task OwnerAdminAndOverrideCanAcceptARequest(int actorId, bool actorIsAdmin, bool overridePermission)
    {
        var group = Group(requests: [8], administrators: actorIsAdmin ? [actorId] : []);
        var store = new RecordingStore(true);
        var (client, sent) = Client(actorId, overridePermission);
        var service = Service(group, store, new(8, "Target", "hr-1"));

        await service.Accept(client, group.Id, 8);

        Assert.True(group.IsMember(8));
        Assert.False(group.HasRequest(8));
        Assert.Equal((group.Id, 8), Assert.Single(store.Accepts));
        Assert.Equal(ServerPacketHeader.GroupMemberUpdatedComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public async Task UnauthorizedActorAndFailedStorePublishNothing()
    {
        var deniedGroup = Group(requests: [8]);
        var deniedStore = new RecordingStore(true);
        var (stranger, deniedPackets) = Client(99, false);
        await Service(deniedGroup, deniedStore, new(8, "Target", "hr-1")).Accept(stranger, deniedGroup.Id, 8);
        Assert.True(deniedGroup.HasRequest(8));
        Assert.Empty(deniedStore.Accepts);
        Assert.Empty(deniedPackets);

        var failedGroup = Group(members: [8]);
        var failedStore = new RecordingStore(false);
        var (owner, failedPackets) = Client(7, false);
        await Service(failedGroup, failedStore, new(8, "Target", "hr-1")).GiveAdmin(owner, failedGroup.Id, 8);
        Assert.False(failedGroup.IsAdmin(8));
        Assert.Empty(failedPackets);

        var declineGroup = Group(requests: [8]);
        var failedDecline = new RecordingStore(false);
        await Service(declineGroup, failedDecline, null).Decline(owner, declineGroup.Id, 8);
        Assert.True(declineGroup.HasRequest(8));
        Assert.Empty(failedPackets);
    }

    [Fact]
    public async Task SettingsStoreFailureLeavesRequestsAndSettingsUnpublished()
    {
        var group = Group(requests: [8]);
        group.Type = GroupType.Locked;
        var (owner, sent) = Client(7, false);
        var service = new GroupSettingsService(
            Proxy<IGroupManager>((method, args) => { args[1] = group; return true; }),
            Proxy<IRoomManager>((method, args) => { args[1] = null; return false; }),
            Proxy<IGroupInfoSnapshotService>((method, _) => throw new NotSupportedException(method)),
            new RecordingSettingsStore(false));

        await service.Update(owner, new(group.Id, 2, 1, true));

        Assert.Equal(GroupType.Locked, group.Type);
        Assert.True(group.HasRequest(8));
        Assert.Equal(0, group.AdminOnlyDeco);
        Assert.False(group.ForumEnabled);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task MissingIdentityUsesUnknownGroupAfterCommittedMutation()
    {
        var group = Group(members: [8]);
        var (owner, sent) = Client(7, false);

        await Service(group, new RecordingStore(true), null).GiveAdmin(owner, group.Id, 8);

        Assert.True(group.IsAdmin(8));
        Assert.Equal(ServerPacketHeader.UnknownGroupComposer, Assert.Single(sent).Header);
    }

    [Fact]
    public void MemberUpdateComposerIsIndependentOfMutableHabboSource()
    {
        var source = new Habbo { Id = 8, Username = "Before", Look = "hr-1" };
        var snapshot = new GroupMemberUpdateSnapshot(9, 1, source.Id, source.Username, source.Look);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var composer = new GroupMemberUpdatedComposer(snapshot);
        client.Send(composer);
        source.Username = "After";
        source.Look = "changed";
        client.Send(composer);

        Assert.Equal(sent[0].Payload, sent[1].Payload);
        var body = sent[0].Payload.AsSpan();
        Assert.Equal(9, BinaryPrimitives.ReadInt32BigEndian(body));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(body[4..]));
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(body[8..]));
        var offset = 12;
        Assert.Equal("Before", ReadString(body, ref offset));
        Assert.Equal("hr-1", ReadString(body, ref offset));
        Assert.Equal(string.Empty, ReadString(body, ref offset));
    }

    [RoomComponentDatabaseFact]
    public void StoreCommitsExactMutationsAndRollsBackMissingRequest()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_gm_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);
            using (var connection = database.Connection())
            {
                connection.Execute("CREATE TABLE group_memberships(user_id INT NOT NULL,group_id INT NOT NULL,`rank` INT NOT NULL DEFAULT 0,PRIMARY KEY(user_id,group_id)); CREATE TABLE group_requests(user_id INT NOT NULL,group_id INT NOT NULL,PRIMARY KEY(user_id,group_id)); CREATE TABLE groups(id INT PRIMARY KEY,`state` ENUM('0','1','2') NOT NULL,admindeco BOOL NOT NULL,forum_enabled BOOL NOT NULL); INSERT INTO groups VALUES(9,'1',0,0); INSERT INTO group_requests VALUES(8,9),(11,9),(12,9),(13,9),(14,10)");
            }
            var store = new GroupMembershipMutationStore(database);
            Assert.True(store.Accept(9, 8));
            Assert.True(store.SetAdmin(9, 8, true));
            using (var verify = database.Connection())
                Assert.Equal(1, verify.ExecuteScalar<int>("SELECT `rank` FROM group_memberships WHERE user_id=8 AND group_id=9"));

            Assert.False(store.Accept(9, 10));
            Assert.True(store.Decline(9, 13));
            var settings = new GroupSettingsStore(database);
            foreach (var type in new[] { GroupType.Open, GroupType.Locked, GroupType.Private })
            {
                Assert.True(settings.Update(9, type, false, false, []));
                using var state = database.Connection();
                var persisted = state.QuerySingle<(string Text, int DomainValue)>(
                    "SELECT CAST(`state` AS CHAR) AS Text, CAST(CAST(`state` AS CHAR) AS UNSIGNED) AS DomainValue FROM groups WHERE id=9");
                Assert.Equal(((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture), persisted.Text);
                Assert.Equal(type, (GroupType)persisted.DomainValue);
            }
            Assert.True(settings.Update(9, GroupType.Private, true, true, [11, 12]));
            Assert.False(settings.Update(10, GroupType.Open, false, false, [14]));
            using var rollback = database.Connection();
            Assert.Equal(0, rollback.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships WHERE user_id=10 AND group_id=9"));
            Assert.Equal(0, rollback.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests WHERE group_id=9"));
            Assert.Equal(1, rollback.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests WHERE group_id=10 AND user_id=14"));
            Assert.Equal((2, true, true), rollback.QuerySingle<(int, bool, bool)>("SELECT `state`,admindeco,forum_enabled FROM groups WHERE id=9"));
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static GroupMembershipMutationService Service(Group group, IGroupMembershipMutationStore store, GroupMemberIdentity? identity) => new(
        Proxy<IGroupManager>((method, args) =>
        {
            if (method != nameof(IGroupManager.TryGetGroup)) throw new NotSupportedException(method);
            args[1] = group;
            return true;
        }),
        Proxy<IRoomManager>((method, args) =>
        {
            if (method != nameof(IRoomManager.TryGetRoom)) throw new NotSupportedException(method);
            args[1] = null;
            return false;
        }),
        Proxy<IGroupMemberIdentityLookup>((method, _) => method == nameof(IGroupMemberIdentityLookup.Find) ? identity : throw new NotSupportedException(method)),
        store);

    private static Group Group(int[]? members = null, int[]? administrators = null, int[]? requests = null) => new(
        9, "Crew", "", "b01014s02024", 42, 7, null, 0, 1, 1, 0, false,
        new((members ?? []).ToImmutableArray(), (administrators ?? []).ToImmutableArray(), (requests ?? []).ToImmutableArray()));

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(int id, bool overridePermission)
    {
        var access = overridePermission ? EditorTestSupport.Access([PermissionKeys.GroupAcceptAny]) : UserAccess.Empty;
        return HabbiconTestSupport.Client(new Habbo { Id = id, Username = "Actor", Access = access });
    }

    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    private static string ReadString(ReadOnlySpan<byte> body, ref int offset)
    {
        var length = BinaryPrimitives.ReadUInt16BigEndian(body[offset..]);
        offset += 2;
        var value = Encoding.UTF8.GetString(body.Slice(offset, length));
        offset += length;
        return value;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!.Name, args!);
    }

    private sealed class RecordingStore(bool succeeds) : IGroupMembershipMutationStore
    {
        public List<(int GroupId, int UserId)> Accepts { get; } = [];
        public bool Accept(int groupId, int userId) { Accepts.Add((groupId, userId)); return succeeds; }
        public bool Decline(int groupId, int userId) => succeeds;
        public bool SetAdmin(int groupId, int userId, bool isAdmin) => succeeds;
    }

    private sealed class RecordingService : IGroupMembershipMutationService
    {
        public List<(string Operation, int GroupId, int UserId)> Calls { get; } = [];
        public Task Accept(GameClient session, int groupId, int userId) { Calls.Add(("accept", groupId, userId)); return Task.CompletedTask; }
        public Task Decline(GameClient session, int groupId, int userId) { Calls.Add(("decline", groupId, userId)); return Task.CompletedTask; }
        public Task GiveAdmin(GameClient session, int groupId, int userId) { Calls.Add(("give", groupId, userId)); return Task.CompletedTask; }
        public Task TakeAdmin(GameClient session, int groupId, int userId) { Calls.Add(("take", groupId, userId)); return Task.CompletedTask; }
    }

    private sealed class RecordingSettingsService : IGroupSettingsService
    {
        public GroupSettingsRequest? Request { get; private set; }
        public Task Update(GameClient session, GroupSettingsRequest request) { Request = request; return Task.CompletedTask; }
    }

    private sealed class RecordingSettingsStore(bool succeeds) : IGroupSettingsStore
    {
        public bool Update(int groupId, GroupType type, bool adminOnlyDeco, bool forumEnabled, ImmutableArray<int> requestsToRemove) => succeeds;
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
