using System.Reflection;
using Plus.Core.Settings;
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

    private sealed class ExpiringClock : TimeProvider
    {
        public DateTimeOffset Expiry { get; } = new(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(5));
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => ++Reads <= 2 ? Expiry.AddTicks(-1) : Expiry;
    }
}
