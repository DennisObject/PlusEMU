using System.Diagnostics;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public class LoginThrottleTests
{
    private readonly ManualTime _time = new(DateTimeOffset.UnixEpoch.AddYears(56));

    private LoginThrottle Throttle(int perAccount = 3, int perAddress = 5, int capacity = 100_000) => new(_time, AuthTestConfig.Options(c =>
    {
        c.MaxFailedLoginsPerAccount = perAccount;
        c.MaxFailedLoginsPerAddress = perAddress;
        c.FailedLoginWindowMinutes = 15;
        c.MaxTrackedLoginFailures = capacity;
    }));

    private static bool Blocked(LoginThrottle throttle, string key, string address) => throttle.BlockedFor(key, address) > TimeSpan.Zero;

    [Fact]
    public void AccountLocksAfterItsFailureLimitFromAnyAddress()
    {
        var throttle = Throttle();
        var dennis = LoginThrottle.AccountKey(7);

        throttle.RecordFailure(dennis, "10.0.0.1");
        throttle.RecordFailure(dennis, "10.0.0.2");
        Assert.False(Blocked(throttle, dennis, "10.0.0.3"));
        throttle.RecordFailure(dennis, "10.0.0.4");

        Assert.True(Blocked(throttle, dennis, "10.0.0.9"));
        Assert.False(Blocked(throttle, LoginThrottle.AccountKey(8), "10.0.0.9"));
    }

    [Fact]
    public void UnknownNamesShareACounterAcrossCaseAccentsAndPadding()
    {
        var throttle = Throttle();

        throttle.RecordFailure(LoginThrottle.UnknownNameKey("Ghöst"), "10.0.0.1");
        throttle.RecordFailure(LoginThrottle.UnknownNameKey(" GHOST"), "10.0.0.2");
        throttle.RecordFailure(LoginThrottle.UnknownNameKey("ghòst"), "10.0.0.3");

        Assert.True(Blocked(throttle, LoginThrottle.UnknownNameKey("Ghost"), "10.0.0.9"));
        Assert.Equal(LoginThrottle.UnknownNameKey(new string('a', 125)), LoginThrottle.UnknownNameKey(new string('a', 4000)));
    }

    [Fact]
    public void AddressLocksAfterItsFailureLimitAcrossAccounts()
    {
        var throttle = Throttle(perAccount: 100, perAddress: 5);

        for (var i = 0; i < 5; i++) {
            throttle.RecordFailure(LoginThrottle.AccountKey(i), "10.0.0.1");
        }

        Assert.True(Blocked(throttle, LoginThrottle.AccountKey(99), "10.0.0.1"));
        Assert.False(Blocked(throttle, LoginThrottle.AccountKey(99), "10.0.0.2"));
    }

    [Fact]
    public void LockLiftsWhenTheWindowEnds()
    {
        var throttle = Throttle();
        var key = LoginThrottle.AccountKey(7);

        for (var i = 0; i < 3; i++) {
            throttle.RecordFailure(key, "10.0.0.1");
        }

        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.True(Blocked(throttle, key, "10.0.0.1"));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(Blocked(throttle, key, "10.0.0.1"));

        throttle.RecordFailure(key, "10.0.0.1");
        Assert.False(Blocked(throttle, key, "10.0.0.1"));
    }

    [Fact]
    public void ReportsHowLongTheLockLasts()
    {
        var throttle = Throttle();
        var key = LoginThrottle.AccountKey(7);

        for (var i = 0; i < 3; i++) {
            throttle.RecordFailure(key, "10.0.0.1");
        }

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(10), throttle.BlockedFor(key, "10.0.0.2"));
        Assert.Equal(TimeSpan.Zero, throttle.BlockedFor(LoginThrottle.AccountKey(8), "10.0.0.2"));
    }

    [Fact]
    public void SuccessClearsTheAccountButNotTheAddress()
    {
        var throttle = Throttle(perAccount: 3, perAddress: 3);
        var victim = LoginThrottle.AccountKey(7);
        throttle.RecordFailure(victim, "10.0.0.1");
        throttle.RecordFailure(victim, "10.0.0.1");

        throttle.RecordSuccess(victim);
        throttle.RecordFailure(victim, "10.0.0.2");
        Assert.False(Blocked(throttle, victim, "10.0.0.3"));

        throttle.RecordFailure(LoginThrottle.AccountKey(8), "10.0.0.1");
        Assert.True(Blocked(throttle, LoginThrottle.AccountKey(9), "10.0.0.1"));
    }

    [Fact]
    public void ManyDistinctFailuresStayCheapToRecord()
    {
        var throttle = Throttle(perAccount: 1000, perAddress: 1000);
        var clock = Stopwatch.StartNew();

        for (var i = 0; i < 30_000; i++) {
            throttle.RecordFailure(LoginThrottle.UnknownNameKey("name" + i), "10.0." + i / 250 + "." + i % 250);
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"30k failures took {clock.Elapsed}");
    }

    [Fact]
    public void AFullTableFailsClosedAndNeverDropsActiveLocks()
    {
        var throttle = Throttle(perAccount: 1, perAddress: 100, capacity: 4);
        throttle.RecordFailure(LoginThrottle.AccountKey(1), "10.0.0.1");
        throttle.RecordFailure(LoginThrottle.AccountKey(2), "10.0.0.2");

        throttle.RecordFailure(LoginThrottle.AccountKey(3), "10.0.0.3");

        Assert.True(Blocked(throttle, LoginThrottle.AccountKey(1), "10.0.0.1"));
        Assert.True(Blocked(throttle, LoginThrottle.AccountKey(2), "10.0.0.2"));
        Assert.True(Blocked(throttle, LoginThrottle.AccountKey(3), "10.0.0.3"));
        Assert.True(Blocked(throttle, LoginThrottle.AccountKey(4), "10.0.0.4"));

        _time.Advance(TimeSpan.FromMinutes(15));
        throttle.RecordFailure(LoginThrottle.AccountKey(5), "10.0.0.5");
        Assert.False(Blocked(throttle, LoginThrottle.AccountKey(4), "10.0.0.4"));
    }
}
