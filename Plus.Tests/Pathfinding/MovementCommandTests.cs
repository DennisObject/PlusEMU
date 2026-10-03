using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class MovementCommandTests
{
    [Fact]
    public async Task DelayedOlderPublicationCannotReplaceTheNewerCommand()
    {
        var slot = new MoveCommandSlot();
        using var captured = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var older = Task.Run(() =>
        {
            var command = new MoveCommand(1, 2, 3, MoveOrigin.User);
            captured.Set();
            Assert.True(release.Wait(5000));
            return slot.Publish(command);
        });
        Assert.True(captured.Wait(5000));
        var newer = new MoveCommand(2, 8, 9, MoveOrigin.Wired, MoveFlags.IgnoreUsers);
        try { Assert.True(slot.Publish(newer)); }
        finally { release.Set(); }
        Assert.False(await older);
        Assert.Same(newer, slot.Read());
    }

    [Fact]
    public void EqualSequenceCannotReplaceAnAlreadyPublishedCommand()
    {
        var slot = new MoveCommandSlot();
        var first = new MoveCommand(7, 1, 2, MoveOrigin.Bot);
        Assert.Null(slot.Read());
        Assert.True(slot.Publish(first));
        Assert.False(slot.Publish(new(7, 9, 9, MoveOrigin.User)));
        Assert.False(slot.Publish(new(6, 9, 9, MoveOrigin.User)));
        Assert.Same(first, slot.Read());
    }

    [Fact]
    public void RepeatedReadsNeverClearTheSlotAndConsumptionUsesTheLastSequence()
    {
        var slot = new MoveCommandSlot();
        slot.Publish(new(1, 1, 1, MoveOrigin.User));
        var latest = new MoveCommand(2, 2, 2, MoveOrigin.User);
        slot.Publish(latest);
        long lastConsumed = 0;
        Assert.Same(latest, ReadNew());
        Assert.Null(ReadNew());
        Assert.Same(latest, slot.Read());
        var next = new MoveCommand(3, 3, 3, MoveOrigin.Interaction, MoveFlags.Teleport);
        slot.Publish(next);
        Assert.Same(next, ReadNew());
        Assert.Null(ReadNew());
        Assert.Same(next, slot.Read());

        MoveCommand? ReadNew()
        {
            var command = slot.Read();
            if (command == null || command.Sequence <= lastConsumed) return null;
            lastConsumed = command.Sequence;
            return command;
        }
    }

    [Fact]
    public void ConcurrentPublishersCoalesceToTheGreatestSequence()
    {
        var slot = new MoveCommandSlot();
        Parallel.For(1, 10001, sequence =>
            slot.Publish(new(sequence, sequence % 256, sequence / 256, MoveOrigin.Bot)));
        Assert.Equal(10000, slot.Read()!.Sequence);
        Assert.Equal((16, 39), (slot.Read()!.X, slot.Read()!.Y));
    }

    [Fact]
    public void MoveCommandCopiesPreserveTheOriginalIntent()
    {
        var original = new MoveCommand(19, 3, 4, MoveOrigin.StaffCommand,
            MoveFlags.IgnoreUsers | MoveFlags.Teleport);
        var replacement = original with { Sequence = 20, X = 5, Origin = MoveOrigin.User };
        Assert.Equal((19L, 3, 4, MoveOrigin.StaffCommand),
            (original.Sequence, original.X, original.Y, original.Origin));
        Assert.Equal((20L, 5, 4, MoveOrigin.User),
            (replacement.Sequence, replacement.X, replacement.Y, replacement.Origin));
        Assert.Equal(MoveFlags.IgnoreUsers | MoveFlags.Teleport, replacement.Flags);
    }

    [Fact]
    public void OwnerScopeUsesRoomIdentityAndRestoresNestedOwners()
    {
        var outer = Room();
        var inner = Room();
        Assert.Null(RoomOwnerScope.CurrentOwner);
        using (RoomOwnerScope.Enter(outer))
        {
            Assert.Same(outer, RoomOwnerScope.CurrentOwner);
            Assert.True(RoomOwnerScope.IsOwner(outer));
            Assert.False(RoomOwnerScope.IsOwner(inner));
            using (RoomOwnerScope.Enter(inner))
            {
                Assert.Same(inner, RoomOwnerScope.CurrentOwner);
                Assert.True(RoomOwnerScope.IsOwner(inner));
                Assert.False(RoomOwnerScope.IsOwner(outer));
            }
            Assert.Same(outer, RoomOwnerScope.CurrentOwner);
        }
        Assert.Null(RoomOwnerScope.CurrentOwner);
    }

    [Fact]
    public void OwnerScopeRestoresThePreviousRoomAfterAnException()
    {
        var outer = Room();
        using (RoomOwnerScope.Enter(outer))
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var scope = RoomOwnerScope.Enter(Room());
                throw new InvalidOperationException("owner callback failed");
            });
            Assert.Same(outer, RoomOwnerScope.CurrentOwner);
            Assert.True(RoomOwnerScope.IsOwner(outer));
        }
        Assert.Null(RoomOwnerScope.CurrentOwner);
    }

    [Fact]
    public void OwnerScopeDoesNotFlowToTaskRun()
    {
        var room = Room();
        using var completed = new ManualResetEventSlim();
        using (RoomOwnerScope.Enter(room))
        {
            var worker = Task.Run(() =>
            {
                var result = (RoomOwnerScope.CurrentOwner, RoomOwnerScope.IsOwner(room));
                completed.Set();
                return result;
            });
            Assert.True(completed.Wait(5000));
            var (owner, isOwner) = worker.GetAwaiter().GetResult();
            Assert.Null(owner);
            Assert.False(isOwner);
            Assert.Same(room, RoomOwnerScope.CurrentOwner);
        }
        Assert.Null(RoomOwnerScope.CurrentOwner);
    }

    [Fact]
    public void RoomQueueDrainsCapturedCommandsInFifoOrderOnlyOnce()
    {
        var actor = Actor();
        var queue = new RoomCommandQueue();
        var commands = new[]
        {
            new RoomCommand(RoomCommandKind.Admit, actor, 41),
            new RoomCommand(RoomCommandKind.ForcePlace, actor, 41, 3, 4, 1.5004,
                ForceResolution.NearestAtOrBelow),
            new RoomCommand(RoomCommandKind.Cancel, actor, 41),
            new RoomCommand(RoomCommandKind.Remove, actor, 41)
        };
        foreach (var command in commands) queue.Enqueue(command);
        var delivered = new List<RoomCommand>();
        queue.Drain(delivered.Add);
        Assert.Equal(commands, delivered);
        Assert.Equal((3, 4, 1.5004, ForceResolution.NearestAtOrBelow),
            (delivered[1].X, delivered[1].Y, delivered[1].Z, delivered[1].Resolution));
        queue.Drain(_ => Assert.Fail("A drained command was delivered twice."));
    }

    [Theory]
    [InlineData(ForceResolution.ExactZ)]
    [InlineData(ForceResolution.NearestAtOrBelow)]
    [InlineData(ForceResolution.Highest)]
    public void RoomQueuePreservesTheForcePlacementResolution(ForceResolution resolution)
    {
        var command = new RoomCommand(RoomCommandKind.ForcePlace, Actor(), 17,
            8, 9, 1.5004, resolution);
        var queue = new RoomCommandQueue();
        queue.Enqueue(command);
        queue.Drain(delivered => Assert.Same(command, delivered));
        Assert.Equal(resolution, command.Resolution);
    }

    [Fact]
    public void RoomQueueDoesNotRejectRemovalForAnEndedLifetime()
    {
        var actor = Actor();
        long currentLifetime = 51;
        var removal = new RoomCommand(RoomCommandKind.Remove, actor, currentLifetime);
        var queue = new RoomCommandQueue();
        queue.Enqueue(removal);
        currentLifetime++;
        var delivered = new List<RoomCommand>();
        queue.Drain(delivered.Add);
        Assert.Same(removal, Assert.Single(delivered));
        Assert.Same(actor, removal.Actor);
        Assert.Equal(51, removal.LifetimeId);
        Assert.NotEqual(currentLifetime, removal.LifetimeId);
    }

    private static Room Room() => (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
    private static RoomUser Actor() => (RoomUser)RuntimeHelpers.GetUninitializedObject(typeof(RoomUser));
}
