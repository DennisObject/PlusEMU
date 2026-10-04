using System.Reflection;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
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
public sealed class AccessControlDatabaseTests : IDisposable
{
    private const int Actor = 940001, Target = 940002, Peer = 940003;
    private const int ActorRole = 940101, LimitedRole = 940102, PeerRole = 940103;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly AccessControl _access;
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
        _database = new(connectionString);
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
        _access = new(_database, clients, NullLogger<AccessControl>.Instance, _clock);
        _access.Init();
        _actor = new() { Id = Actor, Access = _access.Resolve(Actor) };
        _target.Access = _access.Resolve(Target);
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
    public void AUserLoadedBeforeClientRegistrationObservesMutationsAndReloads()
    {
        // Login holds this reference between its data-loading task and client attachment.
        var loaded = _access.Resolve(Target);
        ((Clients)(object)_clients).Registered = false;
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ModerationTool, false, "grant while login is paused"));
        Assert.True(loaded.Can(PermissionKeys.ModerationTool));
        Assert.True(_access.SetOverride(_actor, Target, PermissionKeys.ModerationTool, true, "deny while login is paused"));
        Assert.False(loaded.Can(PermissionKeys.ModerationTool));
        using (var connection = _database.Connection())
            connection.Execute("INSERT INTO user_roles (user_id, role_id) VALUES (@Target, @LimitedRole)", new { Target, LimitedRole });
        _access.Reload();
        Assert.Same(loaded, _access.Resolve(Target));
        Assert.Equal(LimitedRole, loaded.PrimaryRole!.Id);
        Assert.Empty(_sent);
        ((Clients)(object)_clients).Registered = true;
        _access.Refresh(Target);
        Assert.Same(loaded, _target.Access);
        Assert.Single(_sent);
    }

    [AccessControlDatabaseFact]
    public async Task RegistrationUsesRolesAndPreservesTheOldDefaultVipTier()
    {
        var accounts = new AccountStore(_database, _clock, Options.Create(new AuthApiConfiguration()));
        _registeredUserId = await accounts.Create(new("acl_registered", "test-hash", "acl_registered@hotel", "hd-180-1", "M", "127.0.0.1"));
        Assert.NotNull(_registeredUserId);
        var access = _access.Resolve(_registeredUserId.Value);
        Assert.Contains(access.Roles, role => role.Slug == "vip");
        Assert.True(access.Can(PermissionKeys.CommandMimic));
        Assert.Equal(15, access.Limit("limit.daily_respects"));
        using var connection = _database.Connection();
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT `rank` FROM users WHERE id = @userId", new { userId = _registeredUserId.Value }));
        Assert.Equal("registration.role", connection.QuerySingle<string>("SELECT action FROM acl_audit_log WHERE target_id = @userId", new { userId = _registeredUserId.Value }));
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
        new UserInfoCommand(_database, _clients, _access).Execute(session, null!, ["", "acl_target"]);
        Assert.Single(messages);
        Assert.Contains("ACL limited", System.Text.Encoding.UTF8.GetString(messages[0].Payload));
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

    public class Clients : DispatchProxy
    {
        public GameClient Client { get; set; } = null!;
        public bool Registered { get; set; } = true;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            "get_GetClients" => Registered ? new List<GameClient> { Client } : new List<GameClient>(),
            "GetClientByUsername" => Registered && (string)args![0]! == Client.GetHabbo().Username ? Client : null,
            "GetClientByUserId" => Registered && (int)args![0]! == Client.GetHabbo().Id ? Client : null,
            _ => throw new InvalidOperationException($"Unexpected client call {targetMethod.Name}")
        };
    }
}
