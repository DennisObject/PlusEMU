using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public class LoginThrottleTests
{
    private readonly ManualTime _time = new(DateTimeOffset.UnixEpoch.AddYears(56));

    private LoginThrottle Throttle(int perAccount = 3, int perAddress = 5) => new(_time, AuthTestConfig.Options(c =>
    {
        c.MaxFailedLoginsPerAccount = perAccount;
        c.MaxFailedLoginsPerAddress = perAddress;
        c.FailedLoginWindowMinutes = 15;
    }));

    [Fact]
    public void AccountLocksAfterItsFailureLimitWhateverTheCaseOrAddress()
    {
        var throttle = Throttle();

        throttle.RecordFailure("Dennis", "10.0.0.1");
        throttle.RecordFailure("dennis ", "10.0.0.2");
        Assert.False(throttle.IsBlocked("DENNIS", "10.0.0.3"));
        throttle.RecordFailure("dEnNiS", "10.0.0.4");

        Assert.True(throttle.IsBlocked("Dennis", "10.0.0.9"));
        Assert.False(throttle.IsBlocked("Someone", "10.0.0.9"));
    }

    [Fact]
    public void AddressLocksAfterItsFailureLimitAcrossUsernames()
    {
        var throttle = Throttle(perAccount: 100, perAddress: 5);

        for (var i = 0; i < 5; i++)
            throttle.RecordFailure("guess" + i, "10.0.0.1");

        Assert.True(throttle.IsBlocked("fresh", "10.0.0.1"));
        Assert.False(throttle.IsBlocked("fresh", "10.0.0.2"));
    }

    [Fact]
    public void LockLiftsWhenTheWindowEnds()
    {
        var throttle = Throttle();
        for (var i = 0; i < 3; i++)
            throttle.RecordFailure("Dennis", "10.0.0.1");

        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.True(throttle.IsBlocked("Dennis", "10.0.0.1"));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(throttle.IsBlocked("Dennis", "10.0.0.1"));

        throttle.RecordFailure("Dennis", "10.0.0.1");
        Assert.False(throttle.IsBlocked("Dennis", "10.0.0.1"));
    }

    [Fact]
    public void ReportsHowLongTheLockLasts()
    {
        var throttle = Throttle();
        for (var i = 0; i < 3; i++)
            throttle.RecordFailure("Dennis", "10.0.0.1");

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(10), throttle.BlockedFor("Dennis", "10.0.0.2"));
        Assert.Equal(TimeSpan.Zero, throttle.BlockedFor("Other", "10.0.0.2"));
    }

    [Fact]
    public void SuccessClearsTheAccountButNotTheAddress()
    {
        var throttle = Throttle(perAccount: 3, perAddress: 3);
        throttle.RecordFailure("victim", "10.0.0.1");
        throttle.RecordFailure("victim", "10.0.0.1");

        throttle.RecordSuccess("Victim");
        throttle.RecordFailure("victim", "10.0.0.2");
        Assert.False(throttle.IsBlocked("victim", "10.0.0.3"));

        throttle.RecordFailure("other", "10.0.0.1");
        Assert.True(throttle.IsBlocked("anyone", "10.0.0.1"));
    }
}

internal static class LoginThrottleTestExtensions
{
    public static bool IsBlocked(this LoginThrottle throttle, string username, string address) => throttle.BlockedFor(username, address) > TimeSpan.Zero;
}
