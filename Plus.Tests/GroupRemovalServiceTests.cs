using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class GroupRemovalServiceTests
{
    [Fact]
    public async Task HandlersDecodeAndDelegateIncludingOptionalRemovalFlag()
    {
        var calls = new RecordingService();
        await new DeleteGroupEvent(calls).Parse(null!, HabbiconTestSupport.Incoming(9));
        await new ConfirmRemoveGroupMemberEvent(calls).Parse(null!, HabbiconTestSupport.Incoming(9, 8));
        var packet = HabbiconTestSupport.Incoming(9, 8, true);
        await new RemoveGroupMemberEvent(calls).Parse(null!, packet);
        Assert.False(packet.HasDataRemaining());
        await new RemoveGroupMemberEvent(calls).Parse(null!, HabbiconTestSupport.Incoming(9, 8));
        Assert.Equal(new[] { ("delete", 9, 0), ("confirm", 9, 8), ("remove", 9, 8), ("remove", 9, 8) }, calls.Calls);
    }

    [Fact]
    public async Task FailedDeletionLeavesCacheAndFavouriteUnchanged()
    {
        var group = Group();
        var (owner, sent) = Client(7, group.Id);
        var deleted = false;
        var store = new Store { DeleteResult = false };
        await Service(group, store, owner, () => deleted = true).Delete(owner, group.Id);
        Assert.False(deleted);
        Assert.Equal(group.Id, owner.GetHabbo().HabboStats.FavouriteGroupId);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task DeleteCommitsBeforeCacheAndNotificationPublication()
    {
        var group = Group();
        var (owner, sent) = Client(7, group.Id);
        var deleted = false;
        var store = new Store
        {
            BeforeDelete = () =>
            {
                Assert.False(deleted);
                Assert.Equal(group.Id, owner.GetHabbo().HabboStats.FavouriteGroupId);
                Assert.Empty(sent);
            }
        };
        await Service(group, store, owner, () => deleted = true).Delete(owner, group.Id);
        Assert.True(deleted);
        Assert.Equal(0, owner.GetHabbo().HabboStats.FavouriteGroupId);
        Assert.Equal(ServerPacketHeader.GroupDeactivatedComposer, sent[0].Header);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedMembershipWriteLeavesAdminFavouriteAndPacketsUnchanged(bool throws)
    {
        var group = Group();
        var (target, sent) = Client(8, group.Id);
        var store = new Store { RemoveResult = false, ThrowRemove = throws };
        var operation = Service(group, store, target).Remove;

        if (throws) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation(target, group.Id, 8));
        }
        else {
            await operation(target, group.Id, 8);
        }

        Assert.True(group.IsAdmin(8));
        Assert.True(group.IsMember(8));
        Assert.Equal(group.Id, target.GetHabbo().HabboStats.FavouriteGroupId);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task SuccessfulSelfRemovalPersistsBeforeRemovingAdminAndFavourite()
    {
        var group = Group();
        var (target, sent) = Client(8, group.Id);
        var store = new Store
        {
            BeforeRemove = () =>
            {
                Assert.True(group.IsAdmin(8));
                Assert.Equal(group.Id, target.GetHabbo().HabboStats.FavouriteGroupId);
                Assert.Empty(sent);
            }
        };
        await Service(group, store, target).Remove(target, group.Id, 8);
        Assert.False(group.IsMember(8));
        Assert.False(group.IsAdmin(8));
        Assert.Equal(0, target.GetHabbo().HabboStats.FavouriteGroupId);
        Assert.Equal(new[] { ServerPacketHeader.GroupInfoComposer, ServerPacketHeader.RefreshFavouriteGroupComposer }, sent.Select(p => p.Header));
    }

    [Fact]
    public async Task StrangerCannotConfirmOrRemoveAndCreatorCannotLeave()
    {
        var group = Group();
        var store = new Store();
        var (stranger, sent) = Client(99, 0);
        var service = Service(group, store, stranger);
        await service.ConfirmRemove(stranger, group.Id, 8);
        await service.Remove(stranger, group.Id, 8);
        var (owner, ownerSent) = Client(7, 0);
        await service.Remove(owner, group.Id, 7);
        Assert.Equal(0, store.Removes);
        Assert.Equal(0, store.Counts);
        Assert.True(group.IsAdmin(8));
        Assert.Empty(sent);
        Assert.Empty(ownerSent);
    }

    [Fact]
    public async Task RemovalWaitsForAccountPublicationBeforeClearingFavourite()
    {
        var group = Group();
        var (owner, _) = Client(7, 0);
        var (member, _) = Client(8, group.Id);
        var gate = new AccountSessionGate();
        using var entering = new ManualResetEventSlim();
        var sessions = Proxy<IAccountSessionGate>((method, args) =>
        {
            Assert.Equal(nameof(IAccountSessionGate.Enter), method);
            Assert.Equal(8, (int)args[0]!);
            entering.Set();

            return gate.Enter(8);
        });
        var store = new Store
        {
            BeforeRemove = () => Assert.Equal(11, member.GetHabbo().HabboStats.FavouriteGroupId)
        };
        var lease = gate.Enter(8);
        Task removal;

        try {
            removal = Task.Run(() => Service(group, store, member, sessions: sessions).Remove(owner, group.Id, 8));
            Assert.True(entering.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, store.Removes);
            member.GetHabbo().HabboStats.FavouriteGroupId = 11;
        }
        finally {
            lease.Dispose();
        }

        await removal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(group.IsMember(8));
        Assert.Equal(11, member.GetHabbo().HabboStats.FavouriteGroupId);
    }

    [Fact]
    public async Task DeletionReleasesGroupBeforeAccountCleanupAndKeepsNewFavourite()
    {
        var group = Group();
        var (owner, _) = Client(7, group.Id);
        var gate = new AccountSessionGate();
        using var entering = new ManualResetEventSlim();
        var sessions = Proxy<IAccountSessionGate>((method, args) =>
        {
            var id = (int)args[0]!;

            if (id == 7) {
                entering.Set();
            }

            return gate.Enter(id);
        });
        var lease = gate.Enter(7);
        Task deletion;

        try {
            deletion = Task.Run(() => Service(group, new Store(), owner, sessions: sessions).Delete(owner, group.Id));
            Assert.True(entering.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(Monitor.TryEnter(group), "Deleted-group lock was held while waiting for account publication");

            try {
                owner.GetHabbo().HabboStats.FavouriteGroupId = 11;
            }
            finally {
                Monitor.Exit(group);
            }
        }
        finally {
            lease.Dispose();
        }

        await deletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(11, owner.GetHabbo().HabboStats.FavouriteGroupId);
    }

    [RoomComponentDatabaseFact]
    public void StoreRollsBackAllDependentDeletesAndMembershipFavouritePair()
    {
        var root = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        { AllowZeroDateTime = true, ConvertZeroDateTime = true };
        var schema = "task_refactor_tests_gr_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root.ConnectionString);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            root.Database = schema;
            var database = new ProbeDatabase(root.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("""
                CREATE TABLE groups(id INT PRIMARY KEY) ENGINE=InnoDB;
                CREATE TABLE group_memberships(user_id INT,group_id INT,PRIMARY KEY(user_id,group_id)) ENGINE=InnoDB;
                CREATE TABLE group_requests(user_id INT,group_id INT) ENGINE=InnoDB;
                CREATE TABLE rooms(id INT PRIMARY KEY,group_id INT) ENGINE=InnoDB;
                CREATE TABLE user_statistics(id INT PRIMARY KEY,groupid INT) ENGINE=InnoDB;
                CREATE TABLE items_groups(id INT PRIMARY KEY,group_id INT) ENGINE=InnoDB;
                CREATE TABLE items(id INT PRIMARY KEY,user_id INT,room_id INT) ENGINE=InnoDB;
                INSERT INTO groups VALUES(9);
                INSERT INTO group_memberships VALUES(8,9);
                INSERT INTO group_requests VALUES(10,9);
                INSERT INTO rooms VALUES(42,9);
                INSERT INTO user_statistics VALUES(8,9);
                INSERT INTO items_groups VALUES(92,9);
                INSERT INTO items VALUES(92,8,42);
                CREATE TRIGGER fail_group_delete BEFORE DELETE ON groups FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced final delete failure';
                """);
            var store = new GroupRemovalStore(database);
            Assert.Throws<MySqlException>(() => store.Delete(9));
            Assert.Equal((1, 1, 1, 9, 9, 1), connection.QuerySingle<(int, int, int, int, int, int)>("""
                SELECT (SELECT COUNT(*) FROM groups),(SELECT COUNT(*) FROM group_memberships),
                (SELECT COUNT(*) FROM group_requests),(SELECT group_id FROM rooms WHERE id=42),
                (SELECT groupid FROM user_statistics WHERE id=8),(SELECT COUNT(*) FROM items_groups)
                """));
            connection.Execute("CREATE TRIGGER fail_favourite BEFORE UPDATE ON user_statistics FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced favourite failure'");
            Assert.Throws<MySqlException>(() => store.RemoveMember(9, 8, true, true));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships"));
            Assert.Equal(9, connection.ExecuteScalar<int>("SELECT groupid FROM user_statistics WHERE id=8"));
            connection.Execute("DROP TRIGGER fail_favourite; DROP TRIGGER fail_group_delete");
            Assert.Equal(1, store.CountFurniture(8, 42));
            Assert.True(store.RemoveMember(9, 8, true, true));
            Assert.False(store.RemoveMember(9, 8, true, true));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT groupid FROM user_statistics WHERE id=8"));
            Assert.True(store.Delete(9));
            Assert.False(store.Delete(9));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT group_id FROM rooms WHERE id=42"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items_groups"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static Group Group() => new(9, "Crew", "", "b01014s02024", 42, 7, null,
        0, 1, 1, 0, false, new([7], [8], []));

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(int id, int favourite) =>
        HabbiconTestSupport.Client(new Habbo
        {
            Id = id,
            Username = "Actor",
            Access = UserAccess.Empty,
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, favourite, "", 0)
        });

    private static GroupRemovalService Service(Group group, Store store, GameClient online, Action? deleted = null, IAccountSessionGate? sessions = null) => new(
        Proxy<IGroupManager>((method, args) =>
        {
            if (method == nameof(IGroupManager.TryGetGroup)) {
                args[1] = group;

                return true;
            }

            if (method == nameof(IGroupManager.DeleteGroup)) {
                deleted?.Invoke();

                return null;
            }

            throw new NotSupportedException(method);
        }),
        Proxy<IRoomManager>((method, args) => { args[1] = null; return false; }),
        Proxy<ISettingsManager>((_, _) => "50"),
        Proxy<IGameClientManager>((_, args) => (int)args[0]! == online.GetHabbo().Id ? online : null),
        Proxy<IGroupInfoSnapshotService>((_, args) => new GroupInfoSnapshot(group.Id, group.Type, group.Name,
            group.Description, group.Badge, group.RoomId, "HQ", group.MemberCount, "1-1-1970", "Owner",
            false, false, group.IsMember((int)args[1]!), false, 0, true, false)),
        store, sessions ?? new AccountSessionGate());

    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;

        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => InvokeMethod(method!.Name, args!);
    }

    private sealed class Store : IGroupRemovalStore
    {
        public bool DeleteResult { get; set; } = true;
        public bool RemoveResult { get; set; } = true;
        public bool ThrowRemove { get; set; }
        public Action? BeforeDelete { get; set; }
        public Action? BeforeRemove { get; set; }
        public int Removes { get; private set; }
        public int Counts { get; private set; }
        public bool Delete(int groupId)
        {
            BeforeDelete?.Invoke();

            return DeleteResult;
        }
        public bool RemoveMember(int groupId, int userId, bool requireMembership, bool clearFavourite)
        {
            Removes++;
            BeforeRemove?.Invoke();

            if (ThrowRemove) {
                throw new InvalidOperationException("forced persistence failure");
            }

            return RemoveResult;
        }
        public int CountFurniture(int userId, uint roomId)
        {
            Counts++;

            return 4;
        }
    }

    private sealed class RecordingService : IGroupRemovalService
    {
        public List<(string, int, int)> Calls { get; } = [];
        public Task Delete(GameClient session, int groupId)
        {
            Calls.Add(("delete", groupId, 0));

            return Task.CompletedTask;
        }
        public Task ConfirmRemove(GameClient session, int groupId, int userId)
        {
            Calls.Add(("confirm", groupId, userId));

            return Task.CompletedTask;
        }
        public Task Remove(GameClient session, int groupId, int userId)
        {
            Calls.Add(("remove", groupId, userId));

            return Task.CompletedTask;
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
