using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public class AccountSessionGateTests
{
    [Fact]
    public async Task AnAccountIsHeldUntilReleasedWhileOthersProceed()
    {
        var gate = new AccountSessionGate();
        var held = gate.Enter(5);
        var same = gate.EnterAsync(5);
        await Task.Delay(100);
        Assert.False(same.IsCompleted);
        held.Dispose();
        (await same).Dispose();
    }

    [Fact]
    public void WaitingIsBounded()
    {
        var gate = new AccountSessionGate(TimeSpan.FromMilliseconds(50));
        using var held = gate.Enter(5);
        Assert.Throws<TimeoutException>(() => gate.Enter(5));
    }

    [Fact]
    public void RevocationStopsOnlyLoginsThatStartedEarlier()
    {
        var gate = new AccountSessionGate();
        var started = gate.Begin();
        gate.Revoke(5);
        Assert.True(gate.IsRevoked(5, started));
        Assert.False(gate.IsRevoked(6, started));
        Assert.False(gate.IsRevoked(5, gate.Begin()));
    }
}
