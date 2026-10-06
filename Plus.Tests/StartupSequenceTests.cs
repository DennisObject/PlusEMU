using Plus.Core;
using Xunit;

namespace Plus.Tests;

public class StartupSequenceTests
{
    [Fact]
    public async Task IndependentLoadersStartTogetherAndFinishBeforeTheNextPhase()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterStarted = false;
        var first = new Service(10, async () => { firstStarted.SetResult(); await release.Task; });
        var second = new Service(10, async () => { secondStarted.SetResult(); await release.Task; });
        var later = new Service(20, () => { laterStarted = true; return Task.CompletedTask; });

        var startup = StartupSequence.Start([later, first, second]);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task);
        Assert.False(laterStarted);
        release.SetResult();
        await startup;
        Assert.True(laterStarted);
    }

    [Fact]
    public async Task DependenciesFinishBeforeLaterServicesStart()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dependencyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dependentStarted = false;
        var dependency = new Service(10, async () =>
        {
            dependencyStarted.SetResult();
            await release.Task;
        });
        var dependent = new Service(20, () =>
        {
            dependentStarted = true;

            return Task.CompletedTask;
        });

        var startup = StartupSequence.Start([dependent, dependency]);
        await dependencyStarted.Task;
        Assert.False(dependentStarted);
        Assert.False(startup.IsCompleted);
        release.SetResult();
        await startup;
        Assert.True(dependentStarted);
    }

    [Fact]
    public async Task FailedDependencyPreventsLaterServicesStarting()
    {
        var dependentStarted = false;
        var failing = new Service(10, () => Task.FromException(new InvalidOperationException("Load failed")));
        var dependent = new Service(20, () =>
        {
            dependentStarted = true;

            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartupSequence.Start([dependent, failing]));
        Assert.False(dependentStarted);
    }

    private sealed class Service(int order, Func<Task> start) : IStartable
    {
        public int StartOrder => order;
        public Task Start() => start();
    }
}
