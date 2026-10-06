using System.Reflection;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class GameLoopTests
{
    [Fact]
    public async Task StopBeforeStartAndRepeatedStopComplete()
    {
        var game = Create(() => { });
        await Task.Run(game.StopGameLoop).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(game.StopGameLoop).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StopWaitsForTheRunningCycleAndDuplicateStartIsRejected()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var game = Create(() => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); });
        game.StartGameLoop();

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(game.StartGameLoop);
            var stopping = Task.Run(game.StopGameLoop);
            Assert.False(stopping.IsCompleted);
            release.SetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            game.StartGameLoop();
            await Task.Run(game.StopGameLoop).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult();
            game.StopGameLoop();
        }
    }

    [Fact]
    public async Task AFailedCycleIsObservedWithoutHangingStop()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var game = Create(() => { failed.SetResult(); throw new InvalidOperationException("Cycle failed."); });
        game.StartGameLoop();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(game.StopGameLoop).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Cycle failed.", error.Message);
        game.StopGameLoop();
    }

    private static Game Create(Action cycle)
    {
        var rooms = DispatchProxy.Create<IRoomManager, CycleProxy>();
        ((CycleProxy)(object)rooms).Cycle = cycle;
        var clients = DispatchProxy.Create<IGameClientManager, CycleProxy>();

        return new Game(clients, null!, null!, null!, rooms, null!, null!, null!, null!, null!, null!, null!);
    }

    private class CycleProxy : DispatchProxy
    {
        public Action Cycle { get; set; } = () => { };
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "OnCycle")
            {
                throw new NotSupportedException(method?.Name);
            }

            Cycle();

            return null;
        }
    }
}
