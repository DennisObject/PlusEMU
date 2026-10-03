namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class PathWorkspace
{
    internal readonly int[] G, Parent, Stamp, HeapPos, Sequence, HeapNode;
    internal readonly ulong[] HeapKey;
    internal int Generation, Count, NextSequence;
    public int AddressRange { get; }
    public int NodeCapacity { get; }
    public int HeapOperations { get; internal set; }
    public int Expansions { get; internal set; }
    public int CanStepCalls { get; internal set; }
    public long RetainedBytes => G.Length * 20L + HeapNode.Length * 12L;
    private long _leaseToken;
    private long _nextLeaseToken;

    public PathWorkspace(int addressRange, int nodeCount)
    {
        AddressRange = addressRange; NodeCapacity = nodeCount + 1;
        G = new int[addressRange + 1]; Parent = new int[addressRange + 1]; Stamp = new int[addressRange + 1];
        HeapPos = new int[addressRange + 1]; Sequence = new int[addressRange + 1];
        HeapNode = new int[NodeCapacity]; HeapKey = new ulong[NodeCapacity];
    }
    internal void Begin()
    {
        if (++Generation == int.MaxValue) { Array.Clear(Stamp); Generation = 1; }
        Count = NextSequence = HeapOperations = Expansions = CanStepCalls = 0;
    }
    internal long Acquire()
    {
        var token = Interlocked.Increment(ref _nextLeaseToken);
        if (Interlocked.CompareExchange(ref _leaseToken, token, 0) != 0)
            throw new InvalidOperationException("Workspace already leased.");
        return token;
    }
    internal bool Release(long token) => Interlocked.CompareExchange(ref _leaseToken, 0, token) == token;

    internal void Insert(int node, ulong key)
    {
        var p = Count++; HeapNode[p] = node; HeapKey[p] = key; HeapPos[node] = p;
        SiftUp(p); HeapOperations++;
    }
    internal void Decrease(int node, ulong key)
    {
        var p = HeapPos[node]; HeapKey[p] = key; SiftUp(p); HeapOperations++;
    }
    private void SiftUp(int p)
    {
        var node = HeapNode[p]; var key = HeapKey[p];
        while (p > 0)
        {
            var parent = (p - 1) >> 1;
            if (HeapKey[parent] <= key) break;
            HeapNode[p] = HeapNode[parent]; HeapKey[p] = HeapKey[parent]; HeapPos[HeapNode[p]] = p;
            p = parent;
        }
        HeapNode[p] = node; HeapKey[p] = key; HeapPos[node] = p;
    }
    internal int Pop()
    {
        var result = HeapNode[0];
        if (--Count > 0)
        {
            var node = HeapNode[Count]; var key = HeapKey[Count]; var p = 0;
            while (p * 2 + 1 < Count)
            {
                var child = p * 2 + 1;
                if (child + 1 < Count && HeapKey[child + 1] < HeapKey[child]) child++;
                // Floyd's sift: descend to the leaf using child comparisons, then
                // repair upward. The final heap entry usually belongs near a leaf.
                HeapNode[p] = HeapNode[child]; HeapKey[p] = HeapKey[child]; HeapPos[HeapNode[p]] = p;
                p = child;
            }
            HeapNode[p] = node; HeapKey[p] = key; HeapPos[node] = p;
            SiftUp(p);
        }
        HeapPos[result] = -1; HeapOperations++;
        return result;
    }

}

public static class PathWorkspacePool
{
    private static readonly object Sync = new();
    private static readonly Dictionary<(int Address, int Nodes), Pool> Pools = new();
    private const long RetentionLimit = 64 * 1024 * 1024;
    private static long _retainedBytes;
    public static long RetainedBytes { get { lock (Sync) return _retainedBytes; } }

    private sealed class Pool
    {
        internal readonly Stack<PathWorkspace> Available = new();
        internal int Leases;
    }

    public static Lease Rent(int addressRange, int nodeCount)
    {
        lock (Sync)
        {
            var key = (addressRange, nodeCount);
            if (!Pools.TryGetValue(key, out var pool)) Pools[key] = pool = new();
            if (!pool.Available.TryPop(out var workspace)) workspace = new(addressRange, nodeCount);
            else _retainedBytes -= workspace.RetainedBytes;
            pool.Leases++;
            return new(workspace, workspace.Acquire());
        }
    }

    public readonly struct Lease(PathWorkspace workspace, long token) : IDisposable
    {
        public PathWorkspace Workspace => workspace;
        public void Dispose()
        {
            lock (Sync)
            {
                if (!workspace.Release(token)) return;
                var key = (workspace.AddressRange, workspace.NodeCapacity - 1);
                var pool = Pools[key]; pool.Leases--;
                if (_retainedBytes + workspace.RetainedBytes <= RetentionLimit
                    && pool.Available.Count < Math.Max(1, Environment.ProcessorCount))
                {
                    _retainedBytes += workspace.RetainedBytes;
                    pool.Available.Push(workspace);
                }
                else if (pool.Leases == 0 && pool.Available.Count == 0) Pools.Remove(key);
            }
        }
    }
}
