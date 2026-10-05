using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class AccountSessionGateStripeTests
{
    [Fact]
    public async Task MultiAccountAcquisitionHoldsSharedStripesOnce()
    {
        var gate = new AccountSessionGate(TimeSpan.FromSeconds(2));
        using var held = await gate.EnterManyAsync([1, 65, 1]);

        // A second acquisition of the shared stripe must wait, proving it was taken exactly once and is held.
        var blocked = gate.EnterAsync(129);
        Assert.False(blocked.IsCompleted);
        held.Dispose();
        using var released = await blocked.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledSecondAcquisitionReleasesTheFirstStripe()
    {
        var gate = new AccountSessionGate(TimeSpan.FromSeconds(10));
        using var heldSecond = await gate.EnterAsync(4);
        using var cancellation = new CancellationTokenSource();

        // Stripe 3 is taken first, then the wait for stripe 4 is cancelled while it is still held.
        var acquisition = gate.EnterManyAsync([3, 4], cancellation.Token);
        cancellation.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);

        using var reacquired = await gate.EnterAsync(3).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TimedOutSecondAcquisitionReleasesTheFirstStripe()
    {
        var gate = new AccountSessionGate(TimeSpan.FromMilliseconds(200));
        using var heldSecond = await gate.EnterAsync(6);

        await Assert.ThrowsAnyAsync<TimeoutException>(() => gate.EnterManyAsync([5, 6]));

        using var reacquired = await gate.EnterAsync(5).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WraparoundPairsAcquireInStripeOrderAcrossBothDirections()
    {
        var gate = new AccountSessionGate(TimeSpan.FromSeconds(10));
        using var first = await gate.EnterManyAsync([63, 2]);
        var second = gate.EnterManyAsync([65, 66]); // stripes 1 and 2; stripe 2 is held by first
        Assert.False(second.IsCompleted, second.IsFaulted ? second.Exception!.ToString() : "second completed without waiting");
        first.Dispose();
        using var done = await second.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
