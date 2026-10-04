using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public class BoundedPasswordHasherTests
{
    private static BoundedPasswordHasher Bounded(IPasswordHasher inner, int running, int queued) =>
        new(inner, AuthTestConfig.Options(c =>
        {
            c.MaxConcurrentPasswordChecks = running;
            c.MaxQueuedPasswordChecks = queued;
        }));

    [Fact]
    public async Task AFullQueueRefusesNewWorkInsteadOfWaiting()
    {
        var held = new HeldHasher();
        var hasher = Bounded(held, running: 1, queued: 2);
        var running = Task.Run(() => hasher.Hash("first"));
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var queued = new[] { hasher.Hash("a"), hasher.Hash("b") };
        var refused = hasher.Hash("c");

        Assert.All(queued, t => Assert.False(t.IsCompleted));
        await Assert.ThrowsAsync<PasswordCheckQueueFullException>(() => refused);
        held.Release.Set();
        await Task.WhenAll(queued.Append(running));
    }

    [Fact]
    public async Task ACancelledWaiterLeavesTheQueueAndNeverHashes()
    {
        var held = new HeldHasher();
        var hasher = Bounded(held, running: 1, queued: 1);
        var running = Task.Run(() => hasher.Hash("first"));
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var abort = new CancellationTokenSource();

        var abandoned = hasher.Hash("gone", abort.Token);
        abort.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        var next = hasher.Hash("next");
        held.Release.Set();
        await Task.WhenAll(running, next);
        Assert.Equal(2, held.Hashed);
    }
}
