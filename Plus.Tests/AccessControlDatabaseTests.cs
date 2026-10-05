using System.Reflection;
using System.Data;
using Plus.Database;
using Plus.HabboHotel;
using Plus.HabboHotel.Users.Permissions;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Incoming.Moderation;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Subscriptions;
using Xunit;

namespace Plus.Tests;

public sealed class AccessControlDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_ACL_TEST_CONNECTION_STRING";
    public AccessControlDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) == null)
            Skip = $"Set {Variable} to a disposable task_acl_tests_ database with the migrated schema.";
    }
}

[CollectionDefinition("AccessControlDatabase", DisableParallelization = true)]
public sealed class AccessControlDatabaseCollection;

[Collection("AccessControlDatabase")]
public sealed partial class AccessControlDatabaseTests : IDisposable
{
    private const int Actor = 940001, Target = 940002, Peer = 940003;
    private const int ActorRole = 940101, LimitedRole = 940102, PeerRole = 940103;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly AccessControl _access;
    private readonly CountingDatabase _accessDatabase;
    private readonly IGameClientManager _clients;
    private readonly ManualClock _clock = new();
    private int? _registeredUserId;
    private readonly Habbo _actor;
    private readonly Habbo _target;
    private readonly List<(uint Header, byte[] Body)> _sent;

    public AccessControlDatabaseTests()
    {
        var connectionString = Environment.GetEnvironmentVariable(AccessControlDatabaseFactAttribute.Variable)!;
        if (!new MySqlConnectionStringBuilder(connectionString).Database.StartsWith("task_acl_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Access-control tests require a disposable task_acl_tests_ database.");
        var builder = new MySqlConnectionStringBuilder(connectionString) { AllowZeroDateTime = true, ConvertZeroDateTime = true };
        _database = new(builder.ConnectionString);
        using (var connection = _database.Connection())
        {
            connection.Execute("DELETE FROM user_roles WHERE user_id IN @ids; DELETE FROM user_permissions WHERE user_id IN @ids; DELETE FROM users WHERE id IN @ids; " +
                "DELETE FROM roles WHERE id IN @roles", new { ids = new[] { Actor, Target, Peer }, roles = new[] { ActorRole, LimitedRole, PeerRole } });
            connection.Execute("INSERT INTO users (id, username, auth_ticket) VALUES (@Actor, 'acl_actor', ''), (@Target, 'acl_target', ''), (@Peer, 'acl_peer', '')", new { Actor, Target, Peer });
            connection.Execute("INSERT INTO roles (id, slug, name, weight, security_level) VALUES (@ActorRole, 'acl_actor', 'ACL actor', 200, 7), " +
                "(@LimitedRole, 'acl_limited', 'ACL limited', 20, 2), (@PeerRole, 'acl_peer', 'ACL peer', 200, 7)", new { ActorRole, LimitedRole, PeerRole });
            connection.Execute("INSERT INTO role_permissions (role_id, permission_key) VALUES (@ActorRole, '*'), (@LimitedRole, 'camera.use'); " +
                "INSERT INTO user_roles (user_id, role_id) VALUES (@Actor, @ActorRole), (@Peer, @PeerRole)", new { ActorRole, LimitedRole, PeerRole, Actor, Peer });
        }
        _target = new Habbo { Id = Target, Username = "acl_target" };
        var (client, sent) = HabbiconTestSupport.Client(_target);
        _sent = sent;
        var clients = _clients = DispatchProxy.Create<IGameClientManager, Clients>();
        ((Clients)(object)clients).Client = client;
        _target.Client = client;
        _accessDatabase = new(_database);
        _access = new(_accessDatabase, clients, NullLogger<AccessControl>.Instance, _clock);
        _access.Init();
        _actor = new() { Id = Actor, Access = _access.Resolve(Actor) };
        _target.Access = _access.Resolve(Target);
    }

    [AccessControlDatabaseFact]
    public async Task AwaitedStartupAndAdministrativeReloadPublishWithoutHoldingAnAsyncContinuation()
    {
        await _access.Start().WaitAsync(TimeSpan.FromSeconds(10));
        var revision = _access.AdminSnapshot(_actor).Revision;
        var result = await Task.Run(() => _access.Apply(_actor, revision,
            new SaveAccessRole(LimitedRole, "acl_limited", "Reloaded role", "", 20, 2, "", false, false)))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Ok);
        Assert.True(_access.TryGetRole(LimitedRole, out var role));
        Assert.Equal("Reloaded role", role.Name);
        Assert.Contains(_sent, packet => packet.Header == ServerPacketHeader.UserRightsComposer);
    }

    [AccessControlDatabaseFact]
    public void RoleAndOverrideChangesAreAuditedAndPushTheirResolvedWireLive()
    {
        Assert.True(_access.AssignRole(_actor, Target, LimitedRole));
        Assert.Equal(LimitedRole, _target.Access.PrimaryRole!.Id);
        Assert.Equal(ServerPacketHeader.UserRightsComposer, Assert.Single(_sent).Header);
        Assert.True(_access.SetOverride(_actor, Target, "camera.*", true, "temporary camera denial"));
        Assert.False(_target.Access.Can(PermissionKeys.CameraUse));
        Assert.True(_access.SetOverride(_actor, Target, "moderation.*", false, "delegated"));
        Assert.True(_target.Access.Can(PermissionKeys.ModerationTool));
        Assert.True(_access.RemoveOverride(_actor, Target, "camera.*"));
        Assert.True(_target.Access.Can(PermissionKeys.CameraUse));
        Assert.True(_access.RemoveOverride(_actor, Target, "moderation.*"));
        Assert.True(_access.RevokeRole(_actor, Target, LimitedRole));
        Assert.Equal(6, _sent.Count);
        using var connection = _database.Connection();
        var actions = connection.Query<string>("SELECT action FROM acl_audit_log WHERE actor_id = @Actor AND target_id = @Target ORDER BY id", new { Actor, Target }).ToArray();
        Assert.Equal(new[] { "role.assign", "permission.deny", "permission.grant", "permission.remove", "permission.remove", "role.revoke" }, actions);
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT `rank` FROM users WHERE id = @Target", new { Target }));
    }

    [AccessControlDatabaseFact]
    public async Task AccessChangesAndTheirScheduledExpiryResendBothLists()
    {
        using var lists = new ClientAccessLists(_access, ClientAccessListTests.Styles(), ClientAccessListTests.Models());
        await lists.Start();
        // The same sender is used after successful login.
        lists.Send(_target);
        AssertLists([0], ["model_a"]);
        using (var connection = _database.Connection())
            connection.Execute("INSERT INTO role_permissions (role_id, permission_key) VALUES (@LimitedRole, @key)",
                new { LimitedRole, key = PermissionKeys.ChatStyleStaff });
        _access.Reload();
        Assert.True(_access.AssignRole(_actor, Target, LimitedRole));
        AssertLists([0, 2], ["model_a", "model_gated"]);
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ClubAccess, false, "complimentary", _clock.Now.AddSeconds(30)));
        AssertLists([0, 1, 2], ["model_a", "model_gated", "model_hc", "model_vip"]);
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ChatStyleStaff, true, "deny"));
        AssertLists([0, 1], ["model_a", "model_hc", "model_vip"]);
        _clock.Now = _clock.Now.AddSeconds(30);
        _clock.Tick();
        AssertLists([0], ["model_a"]);
        Assert.True(_access.RemoveOverride(_actor, Target, PermissionKeys.ChatStyleStaff));
        AssertLists([0, 2], ["model_a", "model_gated"]);
        Assert.True(_access.RevokeRole(_actor, Target, LimitedRole));
        AssertLists([0], ["model_a"]);

        void AssertLists(int[] styleIds, string[] modelIds)
        {
            var styles = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = _sent.Last(packet => packet.Header == ServerPacketHeader.AllowedChatStylesComposer).Body };
            Assert.Equal(styleIds.Length, styles.ReadInt());
            Assert.Equal(styleIds, Enumerable.Range(0, styleIds.Length).Select(_ => styles.ReadInt()));
            var models = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = _sent.Last(packet => packet.Header == ServerPacketHeader.CreatableRoomModelsComposer).Body };
            Assert.Equal(modelIds.Length, models.ReadInt());
            foreach (var id in modelIds)
            {
                Assert.Equal(id, models.ReadString());
                for (var i = 0; i < 4; i++) models.ReadInt();
            }
            _sent.Clear();
        }
    }

    [AccessControlDatabaseFact]
    public void MutationChecksStoredActorGrantsAndCannotUseAStaleInMemoryPrivilege()
    {
        Assert.False(_access.AssignRole(_actor, Actor, LimitedRole));
        Assert.False(_access.AssignRole(_actor, Peer, LimitedRole));
        Assert.False(_access.AssignRole(_actor, Target, PeerRole));
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'camera.use', 'deny')", new { Actor });
        Assert.True(_actor.Access.Can(PermissionKeys.CameraUse)); // deliberately stale session
        Assert.False(_access.AssignRole(_actor, Target, LimitedRole));
        Assert.False(_access.SetOverride(_actor, Target, "camera.*", false, "attempted escalation"));
        Assert.False(_access.SetOverride(_actor, Actor, PermissionKeys.CameraUse, true, "self edit"));
        Assert.Empty(_sent);
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor AND target_id IN @ids", new { Actor, ids = new[] { Actor, Target, Peer } }));
    }

    [AccessControlDatabaseFact]
    public void RemovingADenyCannotRevealAnActorPermissionTheyDoNotHold()
    {
        Assert.True(_access.AssignRole(_actor, Target, LimitedRole));
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.CameraUse, true, "deny"));
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'camera.use', 'deny')", new { Actor });
        Assert.False(_access.RemoveOverride(_actor, Target, PermissionKeys.CameraUse));
        Assert.False(_target.Access.Can(PermissionKeys.CameraUse));
    }

    [AccessControlDatabaseFact]
    public void ReloadPushesDatabaseRoleChangesAndFlagsUnknownRegistryKeys()
    {
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_roles (user_id, role_id) VALUES (@Target, @LimitedRole); " +
            "INSERT INTO acl_permissions (`key`, category, description) VALUES ('acl_test_orphan', 'test', '')", new { Target, LimitedRole });
        _access.Reload();
        Assert.Equal(LimitedRole, _target.Access.PrimaryRole!.Id);
        Assert.Equal(ServerPacketHeader.UserRightsComposer, Assert.Single(_sent).Header);
        Assert.True(connection.ExecuteScalar<bool>("SELECT is_orphan FROM acl_permissions WHERE `key` = 'acl_test_orphan'"));
        Assert.False(connection.ExecuteScalar<bool>("SELECT is_orphan FROM acl_permissions WHERE `key` = 'camera.use'"));
    }

    [AccessControlDatabaseFact]
    public void ExpiryPrunesAuditsAndPushesTheNewRightsWithoutRelogging()
    {
        Assert.True(_access.AssignRole(_actor, Target, LimitedRole, _clock.Now.AddSeconds(30)));
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ModerationTool, false, "temporary", _clock.Now.AddSeconds(30)));
        Assert.Equal(2, _target.Access.SecurityLevel);
        _clock.Now = _clock.Now.AddSeconds(30);
        Assert.False(_target.Access.Can(PermissionKeys.ModerationTool));
        _access.Outranks(Actor, Target); // a hierarchy read must not cancel the pending live expiry
        _clock.Tick();
        Assert.Equal(1, _target.Access.SecurityLevel);
        Assert.Equal(3, _sent.Count);
        using var connection = _database.Connection();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @Target", new { Target }));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_permissions WHERE user_id = @Target", new { Target }));
        Assert.Equal(new[] { "permission.expire", "role.expire" }, connection.Query<string>(
            "SELECT action FROM acl_audit_log WHERE actor_id IS NULL AND target_id = @Target ORDER BY action", new { Target }).ToArray());
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT `rank` FROM users WHERE id = @Target", new { Target }));
    }

    [AccessControlDatabaseFact]
    public async Task LoginReReadsChangesMadeBetweenLoadingAndClientAttachment()
    {
        ((Clients)(object)_clients).Registered = false;
        var loader = new LoadUserPermissionsTask(_access);
        await loader.Load(_target);
        var loaded = _target.Access;
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ModerationTool, false, "grant while login is paused"));
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ModerationTool, true, "deny while login is paused"));
        using (var connection = _database.Connection())
            connection.Execute("INSERT INTO user_roles (user_id, role_id) VALUES (@Target, @LimitedRole)", new { Target, LimitedRole });
        _access.Reload();
        Assert.Empty(CachedUsers("_resolved"));
        Assert.Empty(CachedUsers("_refreshAt"));
        Assert.Empty(_sent);
        ((Clients)(object)_clients).Registered = true;
        await loader.UserLoggedIn(_target);
        Assert.NotSame(loaded, _target.Access);
        Assert.Equal(LimitedRole, _target.Access.PrimaryRole!.Id);
        Assert.False(_target.Access.Can(PermissionKeys.ModerationTool));
        Assert.Same(_target.Access, _access.Resolve(Target));
        _access.Refresh(Target);
        Assert.Single(_sent);
    }

    [AccessControlDatabaseFact]
    public void OfflinePermissionLimitAndHierarchyReadsDoNotPopulateEitherCache()
    {
        Assert.True(_access.Can(Actor, PermissionKeys.CameraUse));
        Assert.Equal(10, _access.Limit(Peer, "limit.daily_respects"));
        Assert.False(_access.Outranks(Actor, Peer));
        Assert.NotSame(_access.Resolve(Peer), _access.Resolve(Peer));
        Assert.Equal(new[] { Target }, CachedUsers("_resolved"));
        Assert.Equal(new[] { Target }, CachedUsers("_refreshAt"));
    }

    [AccessControlDatabaseFact]
    public void OnlinePermissionLimitAndHierarchyReadsUseCachedSnapshotsWithoutDatabaseReads()
    {
        var clients = (Clients)(object)_clients;
        clients.Additional.Add(HabbiconTestSupport.Client(_actor).Client);
        _actor.Access = _access.Resolve(Actor);
        _accessDatabase.Connections = 0;
        for (var i = 0; i < 50; i++)
        {
            Assert.True(_access.Can(Actor, PermissionKeys.CameraUse));
            Assert.Equal(10, _access.Limit(Target, "limit.daily_respects"));
            Assert.True(_access.Outranks(Actor, Target));
        }
        Assert.Equal(0, _accessDatabase.Connections);
    }

    [AccessControlDatabaseFact]
    public void ReloadReReadsOnlyOnlineUsersAndLeavesOfflineSnapshotsUntracked()
    {
        var offline = _access.Resolve(Actor);
        _access.Resolve(Peer);
        using (var connection = _database.Connection())
            connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'camera.use', 'deny'); " +
                "INSERT INTO user_roles (user_id, role_id) VALUES (@Target, @LimitedRole)", new { Actor, Target, LimitedRole });
        _accessDatabase.Connections = 0;
        _access.Reload();
        Assert.Equal(2, _accessDatabase.Connections); // registry/prune plus the one online account
        Assert.Equal(LimitedRole, _target.Access.PrimaryRole!.Id);
        Assert.Single(_sent);
        Assert.True(offline.Can(PermissionKeys.CameraUse));
        Assert.False(_access.Can(Actor, PermissionKeys.CameraUse));
        Assert.Equal(new[] { Target }, CachedUsers("_resolved"));
        Assert.Equal(new[] { Target }, CachedUsers("_refreshAt"));
    }

    [AccessControlDatabaseFact]
    public void HabboDisposalEvictsBothCachesAndCannotBeReCachedByAnOfflineRead()
    {
        _target.Dispose();
        AssertUncachedAfterLogout();
    }

    [AccessControlDatabaseFact]
    public void ClientDisconnectEvictsBothCachesBeforeLogoutCompletes()
    {
        var field = typeof(PlusEnvironment).GetField("_game", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var game = DispatchProxy.Create<IGame, Game>();
        ((Game)(object)game).Clients = _clients;
        field.SetValue(null, game);
        // Skip persistence already tested by the wallet suites; exercise the real disconnect/unregister path.
        typeof(Habbo).GetField("_habboSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_target, true);
        try
        {
            ((Clients)(object)_clients).Client.OnDisconnected();
            Assert.False(((Clients)(object)_clients).Registered);
            AssertUncachedAfterLogout();
        }
        finally { field.SetValue(null, previous); }
    }

    [AccessControlDatabaseFact]
    public void OldSessionDisposalCannotEvictTheReplacementSession()
    {
        var replacement = new Habbo { Id = Target, Username = "acl_target" };
        ((Clients)(object)_clients).Client = HabbiconTestSupport.Client(replacement).Client;
        var access = _access.Resolve(Target);
        _target.Dispose();
        Assert.Equal(new[] { Target }, CachedUsers("_resolved"));
        Assert.Equal(new[] { Target }, CachedUsers("_refreshAt"));
        _accessDatabase.Connections = 0;
        Assert.True(_access.Can(Target, PermissionKeys.CameraUse));
        Assert.Equal(0, _accessDatabase.Connections);
        Assert.Same(access, replacement.Access);
    }

    private void AssertUncachedAfterLogout()
    {
        Assert.Empty(CachedUsers("_resolved"));
        Assert.Empty(CachedUsers("_refreshAt"));
        Assert.True(_access.Can(Target, PermissionKeys.CameraUse));
        Assert.Empty(CachedUsers("_resolved"));
        Assert.Empty(CachedUsers("_refreshAt"));
        _accessDatabase.Connections = 0;
        _access.Reload();
        Assert.Equal(1, _accessDatabase.Connections); // global registry/prune, no per-user reads
        Assert.Empty(_sent);
    }

    private int[] CachedUsers(string field) => ((System.Collections.IDictionary)typeof(AccessControl)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_access)!).Keys.Cast<int>().Order().ToArray();

    [AccessControlDatabaseFact]
    public async Task AllFiveTicketBroadcastPathsReachOnlyResolvedModerationToolHolders()
    {
        var manager = new GameClientManager(_database, NullLogger<GameClientManager>.Instance);
        var (moderator, updates) = HabbiconTestSupport.Client(_actor);
        var reporter = ((Clients)(object)_clients).Client;
        var (ordinary, ordinaryMessages) = HabbiconTestSupport.Client(new Habbo { Id = Peer, Username = "acl_peer", Access = _access.Resolve(Peer) });
        foreach (var client in new[] { moderator, reporter, ordinary })
        {
            client.Id = Guid.NewGuid();
            manager.RegisterClient(client, client.GetHabbo().Id, client.GetHabbo().Username);
        }
        var moderation = DispatchProxy.Create<IModerationManager, Tickets>();
        var tickets = (Tickets)(object)moderation;
        var field = typeof(PlusEnvironment).GetField("_game", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var game = DispatchProxy.Create<IGame, Game>();
        ((Game)(object)game).Clients = manager;
        field.SetValue(null, game);
        try
        {
            var ticketService = new ModeratorTicketService(moderation, manager, new ModeratorUserLookup(), new ModeratorTicketStore(_database), _clock, null!);
            await new SubmitNewTicketEvent(ticketService).Parse(reporter, HabbiconTestSupport.Incoming("help", 1, Peer, 1, 0));
            await new PickTicketEvent(ticketService).Parse(moderator, HabbiconTestSupport.Incoming(0, 1));
            await new ReleaseTicketEvent(ticketService).Parse(moderator, HabbiconTestSupport.Incoming(1, 1));
            await new PickTicketEvent(ticketService).Parse(moderator, HabbiconTestSupport.Incoming(0, 1));
            await new CloseTicketEvent(ticketService).Parse(moderator, HabbiconTestSupport.Incoming(3, 0, 1));
            await new CallForHelpPendingCallsDeletedEvent(ticketService).Parse(reporter, HabbiconTestSupport.Incoming());
            Assert.Equal(6, updates.Count);
            Assert.All(updates, update => Assert.Equal(ServerPacketHeader.ModeratorSupportTicketComposer, update.Header));
            Assert.Equal(ServerPacketHeader.ModeratorSupportTicketResponseComposer, Assert.Single(_sent).Header);
            Assert.Empty(ordinaryMessages);
            Assert.True(tickets.Ticket!.Answered);
        }
        finally { field.SetValue(null, previous); }
    }





    [AccessControlDatabaseFact]
    public async Task RegistrationStartsWithoutClubOrAnAutomaticVipRole()
    {
        var accounts = new AccountStore(_database, _clock, Options.Create(new AuthApiConfiguration()));
        _registeredUserId = await accounts.Create(new("acl_registered", "test-hash", "acl_registered@hotel", "hd-180-1", "M", "127.0.0.1"));
        Assert.NotNull(_registeredUserId);
        var access = _access.Resolve(_registeredUserId.Value);
        Assert.DoesNotContain(access.Roles, role => role.Slug == "vip");
        Assert.False(access.Can(PermissionKeys.CommandMimic));
        Assert.Equal(0, ClubAccess.LevelFor(access));
        using var connection = _database.Connection();
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT `rank` FROM users WHERE id = @userId", new { userId = _registeredUserId.Value }));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_club_memberships WHERE user_id = @userId", new { userId = _registeredUserId.Value }));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @userId", new { userId = _registeredUserId.Value }));
    }

    [AccessControlDatabaseFact]
    public void MigratedCurrencySubcommandGrantsResolveThroughTheCodeRegistry()
    {
        var access = _access.Resolve(Actor);
        Assert.True(access.Can(PermissionKeys.CommandGiveCoins));
        Assert.True(access.Can(PermissionKeys.CommandGivePixels));
        Assert.True(access.Can(PermissionKeys.CommandGiveDiamonds));
        Assert.True(access.Can(PermissionKeys.CommandGiveGotw));
        using var connection = _database.Connection();
        Assert.Equal(4, connection.ExecuteScalar<int>("SELECT COUNT(DISTINCT permission_key) FROM role_permissions WHERE permission_key IN @keys",
            new { keys = new[] { PermissionKeys.CommandGiveCoins, PermissionKeys.CommandGivePixels, PermissionKeys.CommandGiveDiamonds, PermissionKeys.CommandGiveGotw } }));
    }

    [AccessControlDatabaseFact]
    public void UserInfoReadsMigratedRolesWithoutQueryingLegacyVipColumns()
    {
        Assert.True(_access.AssignRole(_actor, Target, LimitedRole));
        var (session, messages) = HabbiconTestSupport.Client(_actor);
        new UserInfoCommand(new Plus.HabboHotel.Moderation.ModerationUserStore(_database), _clients, _access).Execute(session, null!, ["", "acl_target"]);
        Assert.Single(messages);
        Assert.Contains("ACL limited", System.Text.Encoding.UTF8.GetString(messages[0].Payload));
    }

    [AccessControlDatabaseFact]
    public void ReloadIncludesTheImplicitDefaultRoleInOfflineRankCaches()
    {
        using var connection = _database.Connection();
        var original = connection.ExecuteScalar<int>("SELECT security_level FROM roles WHERE slug = 'default'");
        try
        {
            connection.Execute("UPDATE roles SET security_level = 4 WHERE slug = 'default'");
            _access.Reload();
            Assert.Equal(4, connection.ExecuteScalar<int>("SELECT `rank` FROM users WHERE id = @Target", new { Target }));
            Assert.Equal(4, _access.Resolve(Target).SecurityLevel);
            Assert.Equal(7, connection.ExecuteScalar<int>("SELECT `rank` FROM users WHERE id = @Peer", new { Peer }));
        }
        finally
        {
            connection.Execute("UPDATE roles SET security_level = @original WHERE slug = 'default'", new { original });
            _access.Reload();
        }
    }

    public void Dispose()
    {
        _access.Dispose();
        using var connection = _database.Connection();
        if (_registeredUserId is { } registered)
            connection.Execute("DELETE FROM user_roles WHERE user_id = @registered; DELETE FROM user_statistics WHERE id = @registered; " +
                "DELETE FROM acl_audit_log WHERE target_id = @registered; DELETE FROM users WHERE id = @registered", new { registered });
        connection.Execute("DELETE FROM acl_audit_log WHERE actor_id IN @ids OR target_id IN @ids; " +
            "DELETE FROM user_permissions WHERE user_id IN @ids; DELETE FROM user_roles WHERE user_id IN @ids; " +
            "DELETE FROM user_info WHERE user_id IN @ids; DELETE FROM users WHERE id IN @ids; DELETE FROM roles WHERE id IN @roles; DELETE FROM acl_permissions WHERE `key` = 'acl_test_orphan'",
            new { ids = new[] { Actor, Target, Peer }, roles = new[] { ActorRole, LimitedRole, PeerRole } });
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        private TimerCallback? _callback;
        private object? _state;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new ManualTimer();
        }
        public void Tick() => _callback!(_state);
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CountingDatabase(IDatabase inner) : IDatabase
    {
        public int Connections { get; set; }
        public bool IsConnected() => inner.IsConnected();
#pragma warning disable CS0612
#pragma warning restore CS0612
        public IDbConnection Connection()
        {
            Connections++;
            return inner.Connection();
        }
    }

    public class Tickets : DispatchProxy
    {
        public ModerationTicket? Ticket { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "UserHasTickets": return Ticket != null;
                case "GetTicketBySenderId": return Ticket;
                case "TryAddTicket": Ticket = (ModerationTicket)args![0]!; return true;
                case "TryGetTicket": args![1] = Ticket; return Ticket != null;
                default: throw new InvalidOperationException(method.Name);
            }
        }
    }

    public class Game : DispatchProxy
    {
        public IGameClientManager Clients { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "get_ClientManager"
            ? Clients : throw new InvalidOperationException(method.Name);
    }

    public class Clients : DispatchProxy
    {
        public GameClient Client { get; set; } = null!;
        public List<GameClient> Additional { get; } = new();
        public bool Registered { get; set; } = true;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var clients = Registered ? new[] { Client }.Concat(Additional).ToList() : new List<GameClient>();
            switch (targetMethod!.Name)
            {
                case "get_GetClients": return clients;
                case "GetClientByUsername": return clients.FirstOrDefault(client => client.GetHabbo().Username == (string)args![0]!);
                case "GetClientByUserId": return clients.FirstOrDefault(client => client.GetHabbo().Id == (int)args![0]!);
                case "UnregisterClient": Registered = false; return null;
                default: throw new InvalidOperationException($"Unexpected client call {targetMethod.Name}");
            }
        }
    }
}
