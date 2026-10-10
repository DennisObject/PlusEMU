using System.Reflection;
using Plus.Core.Settings;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;
using Xunit;

namespace Plus.Tests;

public class ClubLimitSnapshotTests
{
    [Theory]
    [InlineData("rooms")]
    [InlineData("friends")]
    [InlineData("visitors")]
    public void MembershipAndRoleLimitShareOneObservationOfAnExpiringGrant(string kind)
    {
        var clock = new ExpiringClock();
        var role = new AccessRole(2, "capacity", "Capacity", 1, 1, "", false,
            [PermissionKeys.ClubAccess], new Dictionary<string, int> { [$"limit.{kind}"] = 11 });
        var access = UserAccess.Create([new(role, clock.Expiry)], clock: clock);
        var settings = DispatchProxy.Create<ISettingsManager, ClubMembershipTests.SettingProxy>();
        ((ClubMembershipTests.SettingProxy)settings).Values[$"club.limit.{kind}.member"] = "99";
        var before = clock.Reads;

        Assert.Equal(11, ClubLimits.For(access, kind, settings));
        Assert.Equal(1, clock.Reads - before);
    }

    [Fact]
    public void VisitorOverrideSharesTheSameObservationAsMembership()
    {
        var clock = new ExpiringClock();
        var role = new AccessRole(2, "capacity", "Capacity", 1, 1, "", false,
            [PermissionKeys.ClubAccess, PermissionKeys.RoomUserLimitOverride], new Dictionary<string, int>());
        var access = UserAccess.Create([new(role, clock.Expiry)], clock: clock);
        var settings = DispatchProxy.Create<ISettingsManager, ClubMembershipTests.SettingProxy>();
        var before = clock.Reads;

        Assert.Equal(int.MaxValue, ClubLimits.For(access, "visitors", settings));
        Assert.Equal(1, clock.Reads - before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModelAndCatalogPageEligibilityShareOneObservationOfAnExpiringGrant(bool roomModel)
    {
        var clock = new ExpiringClock();
        var role = new AccessRole(2, "catalog", "Catalog", 1, 1, "", false,
            [PermissionKeys.ClubAccess, PermissionKeys.CameraUse], new Dictionary<string, int>());
        var access = UserAccess.Create([new(role, clock.Expiry)], clock: clock);
        var model = new RoomModel("model_a", 0, 0, 0, 0, "0", 2, 0, false)
        { RequiredPermission = PermissionKeys.CameraUse };
        var page = new CatalogPage
        { Enabled = true, RequiredClubLevel = 2, RequiredPermission = PermissionKeys.CameraUse };
        var user = new Habbo { Access = access };
        Func<bool> eligible = roomModel ? () => model.CanCreate(access) : () => page.CanOpen(user);
        var before = clock.Reads;

        Assert.True(eligible());
        Assert.Equal(1, clock.Reads - before);
        before = clock.Reads;
        Assert.False(eligible());
        Assert.Equal(1, clock.Reads - before);
    }

    private sealed class ExpiringClock : TimeProvider
    {
        public DateTimeOffset Expiry { get; } = new(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(5));
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => ++Reads <= 2 ? Expiry.AddTicks(-1) : Expiry;
    }
}
