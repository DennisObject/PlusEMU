using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Xunit;

namespace Plus.Tests;

public sealed partial class AccessControlDatabaseTests
{
    private string RunRoleCommand(bool assign, params string[] parameters)
    {
        _actor.Username = "acl_actor";
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        _actor.CurrentRoom = room;
        var (session, sent) = HabbiconTestSupport.Client(_actor);
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, _clock, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty);
        typeof(Room).GetField("_roomUserManager", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(room, manager);
        var user = new RoomUser(Actor, 1, Actor, room, session, TestChatEmotions.Unused, TestRewardProgress.Unused);
        ((ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!)[Actor] = user;
        IChatCommand command = assign ? new GiveRoleCommand(_database, _access, _clock) : new TakeRoleCommand(_database, _access, _clock);
        command.Execute(session, room, parameters);
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal(ServerPacketHeader.WhisperComposer, sent.Single().Header);
        packet.ReadInt();
        return packet.ReadString();
    }

    private void AssertNoRoleCommandMutation()
    {
        using var connection = _database.Connection();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @Target", new { Target }));
        Assert.Empty(_sent);
    }

    [AccessControlDatabaseFact]
    public void GiveRoleCommandAssignsPermanentlyAuditsAndPublishesLiveRights()
    {
        Assert.Contains("permanent", RunRoleCommand(true, "acl_target", "acl_limited"));
        Assert.Equal(LimitedRole, _target.Access.PrimaryRole!.Id);
        Assert.Equal(ServerPacketHeader.UserRightsComposer, Assert.Single(_sent).Header);
        using var connection = _database.Connection();
        Assert.Null(connection.QuerySingle<DateTime?>("SELECT expires_at FROM user_roles WHERE user_id = @Target AND role_id = @LimitedRole", new { Target, LimitedRole }));
        Assert.Equal(Actor, connection.ExecuteScalar<int>("SELECT granted_by FROM user_roles WHERE user_id = @Target AND role_id = @LimitedRole", new { Target, LimitedRole }));
        Assert.Equal("role.assign", connection.QuerySingle<string>("SELECT action FROM acl_audit_log WHERE actor_id = @Actor AND target_id = @Target", new { Actor, Target }));
    }

    [AccessControlDatabaseFact]
    public void RoleCommandsResolveOfflineUsersAndCanRevokeTheirRoles()
    {
        ((Clients)(object)_clients).Registered = false;
        Assert.Contains("Assigned role 'acl_limited'", RunRoleCommand(true, "acl_target", "acl_limited"));
        Assert.Equal(LimitedRole, _access.Resolve(Target).PrimaryRole!.Id);
        Assert.Contains("Revoked role 'acl_limited'", RunRoleCommand(false, "acl_target", "acl_limited"));
        using var connection = _database.Connection();
        Assert.Equal(new[] { "role.assign", "role.revoke" }, connection.Query<string>("SELECT action FROM acl_audit_log WHERE actor_id = @Actor AND target_id = @Target ORDER BY id", new { Actor, Target }));
        Assert.Empty(_sent);
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @Target", new { Target }));
    }

    [AccessControlDatabaseTheory]
    [InlineData(1)]
    [InlineData(3650)]
    public void GiveRoleCommandStoresAndReportsTheRequestedExpiry(int days)
    {
        _clock.Now = DateTimeOffset.FromUnixTimeSeconds(_clock.Now.ToUnixTimeSeconds());
        var expiry = _clock.Now.AddDays(days);
        Assert.Contains($"expires {expiry:yyyy-MM-dd HH:mm:ss} UTC", RunRoleCommand(true, "acl_target", "acl_limited", days.ToString()));
        using var connection = _database.Connection();
        Assert.Equal(expiry.UtcDateTime, connection.QuerySingle<DateTime>("SELECT expires_at FROM user_roles WHERE user_id = @Target AND role_id = @LimitedRole", new { Target, LimitedRole }));
    }

    [AccessControlDatabaseTheory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3651")]
    [InlineData("1.5")]
    [InlineData("not-a-number")]
    [InlineData("2147483648")]
    [InlineData("")]
    public void GiveRoleCommandRejectsInvalidDaysWithoutWriting(string days)
    {
        Assert.Contains("Invalid days", RunRoleCommand(true, "acl_target", "acl_limited", days));
        AssertNoRoleCommandMutation();
    }

    [AccessControlDatabaseTheory]
    [InlineData(true, 200)]
    [InlineData(true, 300)]
    [InlineData(false, 200)]
    [InlineData(false, 300)]
    public void RoleCommandsRejectEqualOrHigherWeightRoles(bool assign, int weight)
    {
        using var connection = _database.Connection();
        connection.Execute("UPDATE roles SET weight = @weight WHERE id = @LimitedRole", new { weight, LimitedRole });
        if (!assign) connection.Execute("INSERT INTO user_roles (user_id, role_id) VALUES (@Target, @LimitedRole)", new { Target, LimitedRole });
        _access.Reload();
        _sent.Clear();
        Assert.Contains("Role change refused", RunRoleCommand(assign, "acl_target", "acl_limited"));
        Assert.Equal(assign ? 0 : 1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @Target", new { Target }));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log WHERE actor_id = @Actor", new { Actor }));
        Assert.Empty(_sent);
    }

    [AccessControlDatabaseTheory]
    [InlineData(true, 200)]
    [InlineData(true, 300)]
    [InlineData(false, 200)]
    [InlineData(false, 300)]
    public void RoleCommandsRejectEqualOrHigherWeightTargets(bool assign, int weight)
    {
        using var connection = _database.Connection();
        connection.Execute("UPDATE roles SET weight = @weight WHERE id = @PeerRole", new { weight, PeerRole });
        _access.Reload();
        _sent.Clear();
        Assert.Contains("Role change refused", RunRoleCommand(assign, "acl_peer", "acl_limited"));
        AssertNoRoleCommandMutation();
    }

    [AccessControlDatabaseTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoleCommandsRejectSelfEditAndTheImplicitDefaultRole(bool assign)
    {
        Assert.Contains("Role change refused", RunRoleCommand(assign, "acl_actor", "acl_limited"));
        Assert.Contains("Role change refused", RunRoleCommand(assign, "acl_target", "default"));
        AssertNoRoleCommandMutation();
    }

    [AccessControlDatabaseTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoleCommandsReportUnknownUsersAndListOnlyAssignableRoleSlugs(bool assign)
    {
        Assert.Contains("Unknown user", RunRoleCommand(assign, "acl_missing", "acl_limited"));
        Assert.Contains("Unknown user", RunRoleCommand(assign, "acl_target' OR 1=1 --", "acl_limited"));
        var reply = RunRoleCommand(assign, "acl_target", "missing-role");
        Assert.Contains("Unknown role", reply);
        Assert.Contains("acl_limited", reply);
        Assert.DoesNotContain("acl_peer", reply);
        Assert.DoesNotContain("acl_actor", reply);
        Assert.DoesNotContain("default", reply);
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, 'camera.use', 'deny')", new { Actor });
        Assert.DoesNotContain("acl_limited", RunRoleCommand(assign, "acl_target", "missing-role"));
        AssertNoRoleCommandMutation();
    }

    [AccessControlDatabaseFact]
    public void TakeRoleCommandRevokesAuditsAndPublishesLiveRights()
    {
        Assert.True(_access.AssignRole(_actor, Target, LimitedRole));
        _sent.Clear();
        Assert.Contains("Revoked role 'acl_limited'", RunRoleCommand(false, "acl_target", "acl_limited"));
        Assert.Equal("default", _target.Access.PrimaryRole!.Slug);
        Assert.Equal(ServerPacketHeader.UserRightsComposer, Assert.Single(_sent).Header);
        using var connection = _database.Connection();
        Assert.Equal("role.revoke", connection.QuerySingle<string>("SELECT action FROM acl_audit_log WHERE actor_id = @Actor AND target_id = @Target ORDER BY id DESC LIMIT 1", new { Actor, Target }));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @Target", new { Target }));
    }

    [AccessControlDatabaseTheory]
    [InlineData(true, "command.giverole")]
    [InlineData(false, "command.takerole")]
    [InlineData(true, "housekeeping.roles.manage")]
    [InlineData(false, "housekeeping.roles.manage")]
    public void RoleCommandsRequireTheirCommandAndManagementPermissions(bool assign, string denied)
    {
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (@Actor, @denied, 'deny')", new { Actor, denied });
        Assert.Contains("not allowed", RunRoleCommand(assign, "acl_target", "acl_limited"));
        AssertNoRoleCommandMutation();
    }

    [AccessControlDatabaseTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoleCommandsReportUsageForMissingOrExtraArguments(bool assign)
    {
        Assert.Contains("Usage:", RunRoleCommand(assign));
        Assert.Contains("Usage:", RunRoleCommand(assign, "acl_target"));
        Assert.Contains("Usage:", RunRoleCommand(assign, "acl_target", "acl_limited", "1", "extra"));
        if (!assign) Assert.Contains("Usage:", RunRoleCommand(false, "acl_target", "acl_limited", "1"));
        AssertNoRoleCommandMutation();
    }
}
