using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Rooms.Instance;

// Furni state writes nobody reported yet, published by the room's Wired pass in the order they were stored.
public partial class WiredComponent
{
    internal const int MaxQueuedStateWrites = 4096;
    private readonly ConcurrentQueue<FurnitureStateTransition> _stateWrites = new();
    private int _queuedStateWrites;
    private int _droppedStateWrites;

    internal void RecordStateTransition(FurnitureStateTransition transition)
    {
        if (Interlocked.Increment(ref _queuedStateWrites) > MaxQueuedStateWrites)
        {
            Interlocked.Decrement(ref _queuedStateWrites);
            Interlocked.Increment(ref _droppedStateWrites);

            return;
        }

        _stateWrites.Enqueue(transition);
    }

    private void PublishStateWrites() => _engine.Mutate(() =>
    {
        if (Interlocked.Exchange(ref _droppedStateWrites, 0) is > 0 and var dropped)
        {
            _logger.LogWarning("Dropped {Count} furni state changes in room {RoomId}: the queue was full", dropped, _room.Id);
            NoteLimit(WiredEngineLimit.PendingStacks,
                $"{dropped} furni state changes were dropped: more than {MaxQueuedStateWrites} waited for the room's next pass.");
        }

        var published = false;

        // At most one queue's worth per pass: writers that keep adding wait for the next pass.
        for (var taken = 0; taken < MaxQueuedStateWrites && _stateWrites.TryDequeue(out var write); taken++)
        {
            Interlocked.Decrement(ref _queuedStateWrites);

            if (!FurnitureStateEvents.Current(write) || !ReferenceEquals(write.Item.GetRoom(), _room) || !write.TryTake())
            {
                continue;
            }

            QueueRuntimeEvent(new(WiredEventKind.StateChanged)
            {
                EventItem = write.Item
            });
            published = true;
        }

        return published;
    });

    private void ClearStateWrites()
    {
        while (_stateWrites.TryDequeue(out var write))
        {
            Interlocked.Decrement(ref _queuedStateWrites);
            write.TryTake();
        }

        Interlocked.Exchange(ref _droppedStateWrites, 0);
    }
}
