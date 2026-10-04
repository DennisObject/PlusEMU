using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Housekeeping;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Permissions;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public sealed class AccessControlDatabaseTheoryAttribute : TheoryAttribute
{
    public AccessControlDatabaseTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable(AccessControlDatabaseFactAttribute.Variable) == null)
            Skip = $"Set {AccessControlDatabaseFactAttribute.Variable} to a disposable task_acl_tests_ database with the migrated schema.";
    }
}

public sealed partial class AccessControlDatabaseTests
{
    public static TheoryData<Type, object[], object[], string> AdminMutations => new()
    {
        { typeof(HousekeepingSaveRoleEvent), [0, "acl_new", "New role", "description", 5, 1, "B", false, true], [0, "acl_new", "New", "", 200, 1, "", false, false], "role.create" },
        { typeof(HousekeepingSaveRoleEvent), [LimitedRole, "ignored", "Updated", "description", 25, 3, "B", false, false], [LimitedRole, "ignored", "Updated", "", 200, 3, "", false, false], "role.update" },
        { typeof(HousekeepingDeleteRoleEvent), [LimitedRole], [PeerRole], "role.delete" },
        { typeof(HousekeepingSetRolePermissionEvent), [LimitedRole, "moderation.*", true], [PeerRole, "moderation.*", true], "role.permission.grant" },
        { typeof(HousekeepingSetRoleLimitEvent), [LimitedRole, "limit.daily_respects", 5, false], [LimitedRole, "limit.daily_respects", 11, false], "role.limit.set" },
        { typeof(HousekeepingAssignRoleEvent), ["acl_target", LimitedRole, 2000000000], ["acl_peer", LimitedRole, 0], "role.assign" },
        { typeof(HousekeepingRevokeRoleEvent), [Target, LimitedRole], [Peer, LimitedRole], "role.revoke" },
        { typeof(HousekeepingSetUserOverrideEvent), ["acl_target", "camera.*", true, "temporary", 2000000000], ["acl_peer", "camera.*", true, "temporary", 0], "permission.deny" },
        { typeof(HousekeepingRemoveUserOverrideEvent), [Target, "camera.use"], [Peer, "camera.use"], "permission.remove" }
    };

    private void PrepareAdminMutation()
    {
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_roles (user_id, role_id) VALUES (@Target, @LimitedRole), (@Peer, @LimitedRole); " +
            "INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Target, 'camera.use', 'deny'), (@Peer, 'camera.use', 'deny')", new { Target, Peer, LimitedRole });
        _access.Resolve(Target);
    }

    private IPacketEvent Handler(Type type)
    {
        var runner = new HousekeepingActionRunner(new HousekeepingAuditLog(_database), NullLogger<HousekeepingActionRunner>.Instance);
        return (IPacketEvent)Activator.CreateInstance(type, type.GetConstructors()[0].GetParameters().Length == 1 ? [_access] : [_access, runner])!;
    }

    private async Task<List<(uint Header, byte[] Payload)>> Dispatch(Type type, object[] fields)
    {
        var (session, sent) = Client(_actor);
        using var packets = new PacketManager([Handler(type)], NullLogger<PacketManager>.Instance);
        var header = (uint)typeof(ClientPacketHeader).GetField(type.Name)!.GetRawConstantValue()!;
        await packets.TryExecutePacket(session, header, Incoming(fields));
        return sent;
    }

    [AccessControlDatabaseTheory]
    [MemberData(nameof(AdminMutations))]
    public async Task EveryAdminMutationAuthorizesAuditsAndPublishesLiveRights(Type type, object[] fields, object[] escalation, string action)
    {
        PrepareAdminMutation();
        var revision = _access.AdminSnapshot(_actor).Revision;
        var sent = await Dispatch(type, [revision, ..fields]);
        var reply = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        reply.ReadString();
        Assert.True(reply.ReadBool());
        using var connection = _database.Connection();
        var audit = connection.QuerySingle<AccessAuditEntry>("SELECT action, target_type AS TargetType, payload FROM acl_audit_log WHERE actor_id = @Actor", new { Actor });
        Assert.Equal(action, audit.Action);
        Assert.Equal(action.StartsWith("role.") && action is not "role.assign" and not "role.revoke" ? "role" : "user", audit.TargetType);
        Assert.NotEqual("{}", audit.Payload);
        Assert.Contains(_sent, packet => packet.Header == ServerPacketHeader.UserRightsComposer);
        Assert.True(_access.AdminSnapshot(_actor).Revision > revision);
        if (action == "role.assign") Assert.Equal(2000000000, _access.Members(_actor, LimitedRole, 0).Members.Single(member => member.Id == Target).ExpiresAt);
        if (action == "permission.deny") Assert.Equal(2000000000, _access.Overrides(_actor, "acl_target").Overrides.Single(row => row.Key == "camera.*").ExpiresAt);
        Assert.NotEmpty(_access.Audit(_actor, 0).Entries);
        if (action == "role.delete") Assert.Equal("ACL limited", _access.Audit(_actor, 0).Entries.Single(row => row.Action == action && row.TargetId == LimitedRole).TargetName);
        if (action == "role.update") Assert.Equal("acl_limited", _access.AdminSnapshot(_actor).Roles.Single(role => role.Id == LimitedRole).Slug);
        connection.Execute("DELETE FROM housekeeping_log WHERE actor_id = @Actor; DELETE FROM roles WHERE slug = 'acl_new'; DELETE FROM acl_permissions WHERE `key` LIKE '%acl_new'", new { Actor });
    }

    [AccessControlDatabaseTheory]
    [MemberData(nameof(AdminMutations))]
    public async Task EveryAdminMutationRefusesEscalationWithoutAnAclWrite(Type type, object[] fields, object[] escalation, string action)
    {
        PrepareAdminMutation();
        var revision = _access.AdminSnapshot(_actor).Revision;
        var sent = await Dispatch(type, [revision, ..escalation]);
        var reply = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        reply.ReadString();
        Assert.False(reply.ReadBool());
        Assert.Equal(0, reply.ReadInt());
        Assert.Equal(HousekeepingErrors.Forbidden, reply.ReadString());
        using var connection = _database.Connection();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
        Assert.False(connection.ExecuteScalar<bool>("SELECT success FROM housekeeping_log WHERE actor_id = @Actor ORDER BY id DESC LIMIT 1", new { Actor }));
        Assert.Empty(_sent);
        connection.Execute("DELETE FROM housekeeping_log WHERE actor_id = @Actor", new { Actor });
    }

    [AccessControlDatabaseTheory]
    [MemberData(nameof(AdminMutations))]
    public async Task EveryAdminMutationIsPermissionGatedBeforeReadingThePacket(Type type, object[] fields, object[] escalation, string action)
    {
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'housekeeping.roles.manage', 'deny')", new { Actor });
        _actor.Access = _access.Resolve(Actor);
        Assert.Empty(await Dispatch(type, []));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
    }

    [AccessControlDatabaseTheory]
    [InlineData(typeof(HousekeepingGetRolesEvent), ServerPacketHeader.HousekeepingRolesComposer)]
    [InlineData(typeof(HousekeepingGetRoleMembersEvent), ServerPacketHeader.HousekeepingRoleMembersComposer)]
    [InlineData(typeof(HousekeepingGetUserOverridesEvent), ServerPacketHeader.HousekeepingUserOverridesComposer)]
    [InlineData(typeof(HousekeepingGetRolesAuditEvent), ServerPacketHeader.HousekeepingRolesAuditComposer)]
    public async Task EveryAdminReadUsesTheManagementGateAndEchoesItsRequest(Type type, uint responseHeader)
    {
        object[] args = type == typeof(HousekeepingGetRolesEvent) ? [42] : type == typeof(HousekeepingGetRoleMembersEvent) ? [42, LimitedRole, 0] : type == typeof(HousekeepingGetUserOverridesEvent) ? [42, "acl_target"] : [42, 0];
        var sent = await Dispatch(type, args);
        var response = Assert.Single(sent);
        Assert.Equal(responseHeader, response.Header);
        Assert.Equal(42, new FlashIncomingPacket { Buffer = response.Payload }.ReadInt());
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'housekeeping.roles.manage', 'deny')", new { Actor });
        _actor.Access = _access.Resolve(Actor);
        Assert.Empty(await Dispatch(type, []));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
    }

    [AccessControlDatabaseFact]
    public void StaleEditsDefaultDeletionAndUnownedGrantsAreRefusedWhileOrphansCanBeRemoved()
    {
        var revision = _access.AdminSnapshot(_actor).Revision;
        Assert.True(_access.Apply(_actor, revision, new ChangeRolePermission(LimitedRole, "acl_missing", false)).Ok);
        Assert.Equal("housekeeping.error.stale_revision", _access.Apply(_actor, revision, new ChangeRolePermission(LimitedRole, "moderation.*", true)).Message);
        revision = _access.AdminSnapshot(_actor).Revision;
        var defaultRole = _access.AdminSnapshot(_actor).Roles.Single(role => role.Slug == "default");
        Assert.False(_access.Apply(_actor, revision, new DeleteAccessRole(defaultRole.Id)).Ok);
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'camera.use', 'deny')", new { Actor });
        Assert.False(_access.Apply(_actor, revision, new ChangeRolePermission(LimitedRole, "camera.use", true)).Ok);
        Assert.False(_access.Apply(_actor, revision, new ChangeRolePermission(LimitedRole, "camera.*", true)).Ok);
        Assert.False(_access.Apply(_actor, revision, new ChangeRolePermission(LimitedRole, "camera.use", false)).Ok);
    }
    [AccessControlDatabaseFact]
    public void NonstaffAdministratorCannotCreatePromoteOrAssignStaffRoles()
    {
        var revision = _access.AdminSnapshot(_actor).Revision;
        Assert.False(_access.Apply(_actor, revision, new SaveAccessRole(0, "acl_staff", "Staff", "", 10, 1, "", true, false)).Ok);
        Assert.False(_access.Apply(_actor, revision, new SaveAccessRole(LimitedRole, "ignored", "Staff", "", 20, 2, "", true, false)).Ok);
        using var connection = _database.Connection();
        connection.Execute("UPDATE roles SET is_staff = 1 WHERE id = @LimitedRole", new { LimitedRole });
        _access.Reload();
        Assert.False(_access.AssignRole(_actor, Target, LimitedRole));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
    }

    [AccessControlDatabaseFact]
    public void RemovingLastDefaultLimitCannotRaiseUsersAboveTheActorsCap()
    {
        using var connection = _database.Connection();
        var defaultId = connection.ExecuteScalar<int>("SELECT id FROM roles WHERE slug = 'default'");
        var previous = connection.QuerySingleOrDefault<int?>("SELECT value FROM role_limits WHERE role_id = @defaultId AND limit_key = 'limit.daily_respects'", new { defaultId });
        try
        {
            connection.Execute("INSERT INTO role_limits (role_id, limit_key, value) VALUES (@defaultId, 'limit.daily_respects', 5) ON DUPLICATE KEY UPDATE value = 5", new { defaultId });
            _access.Reload();
            Assert.False(_access.Apply(_actor, _access.AdminSnapshot(_actor).Revision, new ChangeRoleLimit(defaultId, "limit.daily_respects", 0, true)).Ok);
            Assert.Equal(5, _access.Resolve(Target).Limit("limit.daily_respects", 10));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
        }
        finally
        {
            connection.Execute("DELETE FROM role_limits WHERE role_id = @defaultId AND limit_key = 'limit.daily_respects'", new { defaultId });
            if (previous.HasValue) connection.Execute("INSERT INTO role_limits (role_id, limit_key, value) VALUES (@defaultId, 'limit.daily_respects', @value)", new { defaultId, value = previous.Value });
            _access.Reload();
        }
    }

}
