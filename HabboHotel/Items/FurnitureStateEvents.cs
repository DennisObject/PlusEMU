using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

/// <summary>One actual write of a room furni's legacy state during one placement. It is reported once.</summary>
internal sealed class FurnitureStateTransition(Item item, long placement, long sequence)
{
    private int _taken;
    public Item Item { get; } = item;
    public long Placement { get; } = placement;
    // The writing thread's write counter; 0 for writes a request captured.
    public long Sequence { get; } = sequence;
    public bool TryTake() => Interlocked.Exchange(ref _taken, 1) == 0;
}

/// <summary>
/// Every actual state write of a room's furni raises one StateChanged. A user request captures the writes its
/// interactor makes and reports them with the user. A Wired publisher marks before its own write and reports only
/// a write made after that mark on its thread. Every other write (item cycles, dice results, games) is queued and
/// reported by the room's next Wired pass, in the order it was stored.
/// </summary>
internal static class FurnitureStateEvents
{
    private const int RecentWrites = 32;
    [ThreadStatic] private static StateCapture? _capture;
    [ThreadStatic] private static PlacementScope? _placing;
    [ThreadStatic] private static FurnitureStateTransition?[]? _recent;
    [ThreadStatic] private static int _recentNext;
    [ThreadStatic] private static long _sequence;
    [ThreadStatic] private static long? _followed;

    public static RoomUser? Actor(Room room, GameClient? session) =>
        session?.GetHabbo() is { } habbo ? room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) : null;

    // Called by the item for each actual write, possibly under its NavSync: reads and enqueues only.
    public static void Record(Room room, Item item)
    {
        if (item.IsWired)
        {
            return;
        }

        for (var placing = _placing; placing != null; placing = placing.Previous)
        {
            if (ReferenceEquals(placing.Room, room) && ReferenceEquals(placing.Item, item))
            {
                return;
            }
        }

        // Writes before admission or after removal are not this room's state changes.
        if (!ReferenceEquals(room.GetRoomItemHandler()?.GetItem(item.Id), item))
        {
            return;
        }

        for (var capture = _capture; capture != null; capture = capture.Previous)
        {
            if (ReferenceEquals(capture.Room, room))
            {
                capture.Add(new(item, item.Placement, 0));

                return;
            }
        }

        var transition = new FurnitureStateTransition(item, item.Placement, ++_sequence);
        var recent = _recent ??= new FurnitureStateTransition?[RecentWrites];
        recent[_recentNext] = transition;
        _recentNext = (_recentNext + 1) % RecentWrites;
        room.GetWired()?.RecordStateTransition(transition);
    }

    /// <summary>This thread's write counter, taken before a Wired publisher writes.</summary>
    public static long Mark() => _sequence;

    /// <summary>Takes the write this thread made to the item after the mark, unless it was already reported.</summary>
    public static bool TakeWriteSince(Item item, long mark)
    {
        if (_recent is not { } recent)
        {
            return false;
        }

        for (var age = 1; age <= RecentWrites; age++)
        {
            var index = (_recentNext - age + RecentWrites) % RecentWrites;

            if (recent[index] is not { } transition)
            {
                continue;
            }

            if (transition.Sequence <= mark)
            {
                return false;
            }

            if (!ReferenceEquals(transition.Item, item))
            {
                continue;
            }

            recent[index] = null;

            return Current(transition) && transition.TryTake();
        }

        return false;
    }

    /// <summary>Collects this thread's writes to the room's furni until disposed, then restores the previous scope.</summary>
    public static StateCapture Capture(Room room) => new(room);

    /// <summary>The admitted item's own placement writes are its initial state, not a change.</summary>
    public static IDisposable Placing(Room room, Item item) => new PlacementScope(room, item);

    // Each captured write once, in order, for the placement that made it.
    public static void Publish(Room room, RoomUser? actor, IReadOnlyList<FurnitureStateTransition> transitions)
    {
        foreach (var transition in transitions)
        {
            if (Current(transition))
            {
                Publish(room, actor, transition.Item);
            }
        }
    }

    // One write that is already this caller's to report, for the furni the room still holds.
    public static void Publish(Room room, RoomUser? actor, Item item)
    {
        if (Holds(room, item))
        {
            room.GetWired()?.Dispatch(new(WiredEventKind.StateChanged)
            {
                Actor = Present(room, actor),
                EventItem = item
            });
        }
    }

    /// <summary>While a state write's follow-up runs: the write it follows was made after the mark, on this thread.</summary>
    public static FollowedWrite FollowWrite(long mark) => new(mark);

    /// <summary>Takes the write the running follow-up follows, unless it was captured or already reported.</summary>
    public static bool TakeFollowedWrite(Item item) => _followed is { } mark && TakeWriteSince(item, mark);

    // A follow-up of a sequenced or toggled write reports that write with the user who caused it.
    public static void PublishFollowed(Room room, RoomUser? actor, Item item)
    {
        if (TakeFollowedWrite(item))
        {
            Publish(room, actor, item);
        }
    }

    // The item is still in the placement the write was made in.
    internal static bool Current(FurnitureStateTransition transition) => transition.Item.Placement == transition.Placement
        && transition.Item.GetRoom() is { } room && ReferenceEquals(room.GetRoomItemHandler().GetItem(transition.Item.Id), transition.Item);

    private static bool Holds(Room room, Item item) =>
        ReferenceEquals(item.GetRoom(), room) && ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item);

    // A user who left, or whose visit moved on, is not named; the state still changed.
    internal static RoomUser? Present(Room room, RoomUser? actor) =>
        actor != null && ReferenceEquals(room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor)
            && (actor.IsBot || ReferenceEquals(actor.GetClient()?.GetHabbo()?.CurrentRoom, room)) ? actor : null;

    public readonly struct FollowedWrite : IDisposable
    {
        private readonly long? _previous;
        internal FollowedWrite(long mark)
        {
            _previous = _followed;
            _followed = mark;
        }
        public void Dispose() => _followed = _previous;
    }

    internal sealed class StateCapture : IDisposable
    {
        private readonly List<FurnitureStateTransition> _transitions = [];
        private bool _disposed;
        internal StateCapture(Room room)
        {
            Room = room;
            Previous = _capture;
            _capture = this;
        }
        public Room Room
        {
            get;
        }
        internal StateCapture? Previous
        {
            get;
        }
        public IReadOnlyList<FurnitureStateTransition> Transitions => _transitions;
        internal void Add(FurnitureStateTransition transition) => _transitions.Add(transition);
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _capture = Previous;
        }
    }

    private sealed class PlacementScope : IDisposable
    {
        private bool _disposed;
        public PlacementScope(Room room, Item item)
        {
            Room = room;
            Item = item;
            Previous = _placing;
            _placing = this;
        }
        public Room Room
        {
            get;
        }
        public Item Item
        {
            get;
        }
        public PlacementScope? Previous
        {
            get;
        }
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _placing = Previous;
        }
    }
}
