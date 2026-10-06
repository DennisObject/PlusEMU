using System.Collections.Concurrent;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class NavInputs
{
    private readonly ConcurrentDictionary<uint, NavItemRecord> _records = new();
    private readonly long[] _dirty;
    private long _version;
    public int Width { get; }
    public int Height { get; }
    // Only the room owner accesses AppliedRecords.
    internal Dictionary<uint, NavItemRecord> AppliedRecords { get; } = new();
    internal Action<uint>? ItemPublished { get; set; }
    internal IEnumerable<uint> ItemIds => _records.Keys;
    internal NavItemRecord? Read(uint id) => _records.GetValueOrDefault(id);

    public NavInputs(int width, int height)
    {
        Width = width;
        Height = height;
        _dirty = new long[(width * height + 63) / 64];
    }

    public void Attach(Item item)
    {
        item.EnableNavigationSynchronization();

        lock (item.NavSync) {
            item.NavigationInputs = this;
            PublishCurrent(item);
        }
    }

    public void Remove(Item item)
    {
        lock (item.NavSync) {
            var old = Read(item.Id);

            if (old != null) {
                Publish(old with { Version = Interlocked.Increment(ref _version), Removed = true });
            }

            item.NavigationInputs = null;
        }
    }

    // Field-only transaction; room callbacks must run after this returns.
    internal T Mutate<T>(Item item, Func<T> mutation)
    {
        lock (item.NavSync) {
            item.NavMutationDepth++;

            try {
                return mutation();
            }
            finally {
                if (--item.NavMutationDepth == 0 && item.NavigationInputs == this) {
                    PublishCurrent(item);
                }
            }
        }
    }

    internal void PublishCurrent(Item item)
    {
        // All callers hold NavSync, including setters and UpdateState.
        if (item.NavMutationDepth == 0 && item.NavigationInputs == this) {
            var previous = Read(item.Id);
            var record = NavItemRecord.Capture(item, Interlocked.Increment(ref _version), Width, Height, previous);

            if (!ReferenceEquals(record, previous)) {
                Publish(record);
            }
        }
    }

    internal bool Publish(NavItemRecord next)
    {
        while (true) {
            var old = Read(next.ItemId);

            if (old != null && old.Version >= next.Version) {
                return false;
            }

            if (old == null ? !_records.TryAdd(next.ItemId, next) : !_records.TryUpdate(next.ItemId, next, old)) {
                continue;
            }

            // Install first, then mark both footprints. A racing drain cannot lose a move.
            if (old != null) {
                foreach (var t in old.Footprint) {
                    MarkDirty(t);
                }
            }

            foreach (var t in next.Footprint) {
                MarkDirty(t);
            }

            ItemPublished?.Invoke(next.ItemId);

            return true;
        }
    }

    public void MarkDirty(int tile) => Interlocked.Or(ref _dirty[tile >> 6], 1L << (tile & 63));
    public void MarkAllDirty()
    {
        for (var t = 0; t < Width * Height; t++) {
            MarkDirty(t);
        }
    }

    internal HashSet<int> Drain(Action<int>? afterWord = null)
    {
        var result = new HashSet<int>();

        for (var w = 0; w < _dirty.Length; w++) {
            var bits = (ulong)Interlocked.Exchange(ref _dirty[w], 0);

            while (bits != 0) {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                result.Add(w * 64 + bit);
                bits &= bits - 1;
            }

            afterWord?.Invoke(w);
        }

        return result;
    }
}
