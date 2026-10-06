using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.Permissions;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public sealed class AccessControlTests
{
    private static readonly string[] Registry = [PermissionKeys.ModerationTool, PermissionKeys.ModerationBan,
        PermissionKeys.ModerationBanSoft, PermissionKeys.CameraUse, PermissionKeys.HousekeepingRolesManage, PermissionKeys.Ambassador];

    private static AccessRole Role(int id, int weight, string[] grants, int security = 1, params (string Key, int Value)[] limits) =>
        new(id, $"role_{id}", $"Role {id}", weight, security, $"B{id}", weight > 10, grants,
            limits.ToDictionary(limit => limit.Key, limit => limit.Value));

    private static UserAccess Access(AccessRole role, params UserPermissionOverride[] overrides) =>
        UserAccess.Create([new(role)], overrides, Registry);

    [Fact]
    public void WildcardGrantsExpandToConcreteRegisteredKeysOnly()
    {
        var access = Access(Role(1, 10, ["moderation.*"]));
        Assert.Equal(Registry.Where(key => key.StartsWith("moderation.")).Order(), access.Keys.Order());
        Assert.False(access.Can("moderation.unknown"));
        Assert.False(access.Can("moderation.*"));
        Assert.False(access.Can("moderationx.tool"));
        Assert.Equal(Registry.Order(), Access(Role(1, 10, ["*"])).Keys.Order());
    }

    [Fact]
    public void DeniesOverrideRoleAndUserGrantsRegardlessOfSpecificity()
    {
        var access = Access(Role(1, 10, ["*"]), new(PermissionKeys.ModerationBan, false), new UserPermissionOverride("moderation.*", true));
        Assert.True(access.Can(PermissionKeys.CameraUse));
        Assert.False(access.Can(PermissionKeys.ModerationBan));
        Assert.False(access.Can(PermissionKeys.ModerationTool));
        access = Access(Role(1, 10, ["moderation.*"]), new UserPermissionOverride(PermissionKeys.ModerationBan, true));
        Assert.True(access.Can(PermissionKeys.ModerationBanSoft));
        Assert.False(access.Can(PermissionKeys.ModerationBan));
    }

    [Fact]
    public void MultiRoleResolutionUnionsPermissionsAndTakesTheMaximumLimitAndSecurity()
    {
        var first = Role(1, 10, [PermissionKeys.CameraUse], 7, ("limit.daily_respects", 10));
        var second = Role(2, 20, [PermissionKeys.ModerationTool], 3, ("limit.daily_respects", 20));
        var access = UserAccess.Create([new(first), new(second)], registry: Registry);
        Assert.True(access.Can(PermissionKeys.CameraUse));
        Assert.True(access.Can(PermissionKeys.ModerationTool));
        Assert.Equal(20, access.Limit("limit.daily_respects", 99));
        Assert.Equal(23, access.Limit("limit.absent", 23));
        Assert.Equal(7, access.SecurityLevel);
        Assert.Equal(second, access.PrimaryRole);
    }

    [Fact]
    public void ExpiriesInvalidateAnAlreadyCompiledUserWithoutReloading()
    {
        var clock = new TestClock();
        var permanent = Role(1, 0, [PermissionKeys.CameraUse], 1, ("limit.daily_respects", 10));
        var expiring = Role(2, 50, ["moderation.*"], 5, ("limit.daily_respects", 20));
        var access = UserAccess.Create([new(permanent), new(expiring, clock.Now.AddMinutes(1))],
            [new(PermissionKeys.CameraUse, true, clock.Now.AddSeconds(30)), new(PermissionKeys.Ambassador, false, clock.Now.AddMinutes(1))], Registry, clock);
        Assert.False(access.Can(PermissionKeys.CameraUse));
        Assert.True(access.Can(PermissionKeys.ModerationTool));
        clock.Now = clock.Now.AddSeconds(30);
        Assert.True(access.Can(PermissionKeys.CameraUse));
        clock.Now = clock.Now.AddSeconds(30);
        Assert.False(access.Can(PermissionKeys.ModerationTool));
        Assert.False(access.Can(PermissionKeys.Ambassador));
        Assert.Equal(10, access.Limit("limit.daily_respects"));
        Assert.Equal(1, access.SecurityLevel);
        Assert.Equal(permanent, access.PrimaryRole);
    }

    [Fact]
    public void AlreadyExpiredInputsAreIgnoredIncludingAtTheExactBoundary()
    {
        var clock = new TestClock();
        var access = UserAccess.Create([new(Role(1, 90, ["*"], 7), clock.Now)],
            [new(PermissionKeys.CameraUse, false, clock.Now)], Registry, clock);
        Assert.Empty(access.Keys);
        Assert.Empty(access.Roles);
        Assert.Null(access.PrimaryRole);
        Assert.Equal(1, access.SecurityLevel);
    }

    [Fact]
    public void HierarchyUsesWeightAloneAndRequiresStrictSuperiority()
    {
        var actor = Access(Role(1, 50, [], 1));
        Assert.True(actor.Outranks(Access(Role(2, 20, ["*"], 7))));
        Assert.False(actor.Outranks(Access(Role(2, 50, [], 7))));
        Assert.False(actor.Outranks(Access(Role(2, 60, [], 1))));
        Assert.False(Access(Role(1, 90, [], 7)).Can(PermissionKeys.ModerationTool));
    }

    [Fact]
    public void StaffEditsRequireTheManagementRightHierarchyAndAnotherAccount()
    {
        var actor = Access(Role(9, 90, [PermissionKeys.HousekeepingRolesManage]));
        var target = Access(Role(1, 10, []));
        Assert.True(AccessMutationPolicy.CanEdit(9, 1, actor, target));
        Assert.False(AccessMutationPolicy.CanEdit(9, 9, actor, target));
        Assert.False(AccessMutationPolicy.CanEdit(9, 1, Access(Role(9, 90, [])), target));
        Assert.False(AccessMutationPolicy.CanEdit(9, 1, actor, Access(Role(1, 90, []))));
    }

    [Fact]
    public void AssigningARoleCannotEscalateWeightOrAnyExpandedPermission()
    {
        var actor = Access(Role(9, 90, [PermissionKeys.HousekeepingRolesManage, "moderation.*"]));
        var target = Access(Role(1, 10, []));
        Assert.True(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 20, ["moderation.*", "orphan.old_command"]), Registry));
        Assert.False(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 90, []), Registry));
        Assert.False(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 100, []), Registry));
        Assert.False(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 20, ["*"]), Registry));
        Assert.False(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 20, [PermissionKeys.CameraUse]), Registry));
        Assert.False(AccessMutationPolicy.CanAssign(9, 9, actor, target, Role(2, 20, []), Registry));
        var denied = Access(Role(9, 90, ["*"]), new UserPermissionOverride(PermissionKeys.ModerationBan, true));
        Assert.False(AccessMutationPolicy.CanGrant(denied, "moderation.*", Registry));
        Assert.False(AccessMutationPolicy.CanGrant(actor, "unknown.*", Registry));
    }

    [Fact]
    public void AssigningARoleCannotEscalateSecurityOrLimits()
    {
        var actor = Access(Role(9, 90, ["*"], 2, ("limit.daily_respects", 5)));
        var target = Access(Role(1, 10, []));
        Assert.False(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 20, [], 7), Registry));
        Assert.False(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 20, [], 2, ("limit.daily_respects", 100)), Registry));
        Assert.True(AccessMutationPolicy.CanAssign(9, 1, actor, target, Role(2, 20, [], 2, ("limit.daily_respects", 5)), Registry));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(4, true, false)]
    [InlineData(5, true, true)]
    [InlineData(9, true, true)]
    public void ExpandedThresholdSeedsPreserveMinimumRankSemantics(int migratedRank, bool camera, bool moderation)
    {
        // The SQL expands each old threshold onto each individual role; higher weight grants nothing by itself.
        var roleGrants = new List<string>();

        if (migratedRank >= 4) {
            roleGrants.Add(PermissionKeys.CameraUse);
        }

        if (migratedRank >= 5) {
            roleGrants.Add(PermissionKeys.ModerationTool);
        }

        var access = Access(Role(migratedRank, migratedRank * 10, roleGrants.ToArray()));
        Assert.Equal(camera, access.Can(PermissionKeys.CameraUse));
        Assert.Equal(moderation, access.Can(PermissionKeys.ModerationTool));
    }

    [Fact]
    public void WireUsesIndependentClubSecurityAndPrimaryRoleAndExpandedDeniedKeys()
    {
        var role = Role(9, 90, ["*"], 7);
        var access = Access(role, new UserPermissionOverride("moderation.*", true));
        var packet = new RecordingPacket();
        new UserRightsComposer(UserRightsSnapshot.Capture(access)).Compose(packet);
        Assert.Equal(new object[] { 0, 7, true, 9, "Role 9", "B9", 3, "ambassador", 1, "camera.use", 1, "housekeeping.roles.manage", 1 }, packet.Writes);
    }

    [Fact]
    public void BroadcastFiltersRequireARegisteredPermissionDefinition()
    {
        Assert.Same(PermissionKeys.All.Single(permission => permission.Key == PermissionKeys.ModerationTool),
            PermissionKeys.Definition(PermissionKeys.ModerationTool));
        Assert.Throws<ArgumentException>(() => PermissionKeys.Definition("mod_tool"));
        var parameter = typeof(Plus.HabboHotel.GameClients.IGameClientManager).GetMethod("SendPacket")!.GetParameters()[1];
        Assert.Equal(typeof(PermissionDefinition), parameter.ParameterType);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
