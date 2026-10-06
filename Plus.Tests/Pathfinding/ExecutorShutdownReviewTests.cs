using System.Threading;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("room")]
    [InlineData("manager")]
    [InlineData("navigation")]
    public void TeardownWaitsForTheActiveRoomOwnerBeforeMutatingMovement(string teardown)
    {
        var origin = ExecutorFloor(10, 0, 1);
        var actor = ExecutorActor(0, 1);
        InitializeExternalUnloadCollections();
        var navigation = _room.GetGameMap().Navigation!;
        using var race = new ReviewDisposalRace(() => ReviewTeardown(teardown, navigation));
        ReviewObserveWalkOff((_, item) => { if (item == origin) { race.BlockTick(); } });
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.True(RoomCycle.TryStart(_room, _room.ProcessRoom));

        try
        {
            race.StartDisposal();
            Assert.Equal(NavState.Active, actor.Movement.State);
            Assert.False(_room.MDisposed);
            Assert.NotNull(_room.GetGameMap());
            Assert.True(actor.Movement.HasIntent);
        }
        finally { race.Finish(_room.ProcessTask); }

        Assert.Equal(NavState.Removing, actor.Movement.State);
        Assert.All(navigation.Executor.Context.Claims.TileCount, count => Assert.Equal(0, count));
        Assert.Equal(0, DrainRemainingSearches(navigation));

        if (teardown == "room")
        {
            Assert.Null(_room.GetGameMap());
        }
    }

    [Fact]
    public void TeardownFromTheRoomOwnerDoesNotWaitForItsOwnProcessTask()
    {
        var origin = ExecutorFloor(10, 0, 1);
        var actor = ExecutorActor(0, 1);
        InitializeExternalUnloadCollections();
        var navigation = _room.GetGameMap().Navigation!;
        ReviewObserveWalkOff((_, item) => { if (item == origin) { _room.Dispose(); } });
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.True(RoomCycle.TryStart(_room, _room.ProcessRoom));
        _room.ProcessTask.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Assert.True(_room.MDisposed);
        Assert.Null(_room.GetGameMap());
        Assert.Equal(NavState.Removing, actor.Movement.State);
        Assert.All(navigation.Executor.Context.Claims.TileCount, count => Assert.Equal(0, count));
    }

    private void ReviewTeardown(string teardown, RoomNavigation navigation)
    {
        if (teardown == "room")
        {
            _room.Dispose();
        }
        else if (teardown == "manager")
        {
            _room.GetRoomUserManager().Dispose();
        }
        else
        {
            navigation.Shutdown();
        }
    }

    private sealed class ReviewDisposalRace(Action teardown) : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly ManualResetEventSlim _finished = new();
        private Thread? _disposer;
        private Exception? _error;
        public void BlockTick()
        {
            _entered.Set();
            Assert.True(_release.Wait(TimeSpan.FromSeconds(10)));
        }
        public void StartDisposal()
        {
            Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)));
            _disposer = new Thread(() =>
            {
                try
                {
                    teardown();
                }
                catch (Exception error) { _error = error; }
                finally { _finished.Set(); }
            });
            _disposer.Start();
            Assert.True(SpinWait.SpinUntil(() => _finished.IsSet
                || (_disposer.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)));
        }
        public void Finish(Task tick)
        {
            _release.Set();

            if (_disposer != null)
            {
                Assert.True(_disposer.Join(TimeSpan.FromSeconds(10)));
            }

            tick.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.Null(_error);
        }
        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
            _finished.Dispose();
        }
    }
}
