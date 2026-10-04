using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class NameChangePolicyTests
{
    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(0, 1, 700000, false)]
    [InlineData(1, 1, 604800, false)]
    [InlineData(1, 1, 604801, true)]
    [InlineData(7, 1, 86400, false)]
    [InlineData(7, 1, 86401, true)]
    [InlineData(604800, 1, 1, false)]
    [InlineData(604800, 1, 2, true)]
    public void QuotaUsesElapsedTimeAndSupportsOneLifetimeChange(int frequency, int lastChange, int now, bool allowed)
    {
        var role = new AccessRole(1, "member", "Member", 0, 1, "", false, [],
            new Dictionary<string, int> { ["limit.name_change_frequency"] = frequency });
        var habbo = new Habbo { LastNameChangedAt = lastChange == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(lastChange), Access = UserAccess.Create([new(role)]) };
        Assert.Equal(allowed, NameChangePolicy.CanChange(habbo, DateTimeOffset.FromUnixTimeSeconds(now)));
    }
}
