using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Xunit;

namespace Plus.Tests;

public class NameChangePolicyTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void FlagCommandUsesOneInjectedInstantAtTheCooldownBoundary(int seconds, bool allowed)
    {
        var now = new DateTimeOffset(2040, 1, 8, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);
        var clock = new CountingClock(now);
        var role = new AccessRole(1, "member", "Member", 0, 1, "", false, [],
            new Dictionary<string, int> { ["limit.name_change_frequency"] = 1 });
        var habbo = new Habbo
        {
            Id = 7, Username = "Member", Look = "hr-1", Gender = "M", Motto = "",
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0),
            LastNameChangedAt = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Access = UserAccess.Create([new(role)])
        };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        new FlagMeCommand(clock).Execute(client, null!, []);
        Assert.Equal(allowed, habbo.ChangingName);
        Assert.Equal(1, clock.Reads);
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }

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
