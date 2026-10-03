namespace Plus.HabboHotel.Rooms.PathFinding;

public static class PathFinder
{
    public static Vector2D[] DiagMovePoints =
    {
        new(0, -1),
        new(1, -1),
        new(1, 0),
        new(1, 1),
        new(0, 1),
        new(-1, 1),
        new(-1, 0),
        new(-1, -1)
    };

    public static Vector2D[] NoDiagMovePoints =
    {
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    };

    public static List<Vector2D> FindPath(RoomUser user, bool diag, Gamemap map, Vector2D start, Vector2D end)
        => FindPath(user, diag, map, start, end, null, null);

    internal static List<Vector2D> FindPath(RoomUser user, bool diag, Gamemap map, Vector2D start, Vector2D end, PathFinderMetrics? metrics)
        => FindPath(user, diag, map, start, end, metrics, null);

    // Blocked-route fallback: uncapped, and additionally rejects edges the admission check refuses.
    internal static List<Vector2D> FindPath(RoomUser user, bool diag, Gamemap map, Vector2D start, Vector2D end,
        Func<Vector2D, Vector2D, bool, bool> admit) => FindPath(user, diag, map, start, end, null, admit);

    private static List<Vector2D> FindPath(RoomUser user, bool diag, Gamemap map, Vector2D start, Vector2D end,
        PathFinderMetrics? metrics, Func<Vector2D, Vector2D, bool, bool>? admit)
    {
        if (metrics != null) metrics.Expansions = metrics.CanStepCalls = metrics.HeapOperations = 0;
        var path = new List<Vector2D>();
        var nodes = FindPathReversed(user, diag, map, start, end, metrics, admit);
        if (nodes != null)
        {
            path.Add(end);
            while (nodes.Next != null)
            {
                path.Add(nodes.Next.Position);
                nodes = nodes.Next;
            }
        }
        return path;
    }

    public static PathFinderNode FindPathReversed(RoomUser user, bool diag, Gamemap map, Vector2D start,
        Vector2D end) => FindPathReversed(user, diag, map, start, end, null, null);

    private static PathFinderNode FindPathReversed(RoomUser user, bool diag, Gamemap map, Vector2D start,
        Vector2D end, PathFinderMetrics? metrics, Func<Vector2D, Vector2D, bool, bool>? admit)
    {
        if (!map.ValidTile(start.X, start.Y) || !map.ValidTile(end.X, end.Y))
            return null;
        var nodes = new PathFinderNode[map.Model.MapSizeX, map.Model.MapSizeY];
        // Queue priorities are immutable snapshots; improved nodes are inserted again.
        var open = new PriorityQueue<(PathFinderNode Node, int Cost), (int Score, int Distance, long Order)>();
        long order = 0;
        var first = new PathFinderNode(start) { Cost = 0 };
        nodes[start.X, start.Y] = first;
        Enqueue(first);
        while (open.TryDequeue(out var entry, out _))
        {
            if (metrics != null) metrics.HeapOperations++;
            var current = entry.Node;
            if (current.InClosed || entry.Cost != current.Cost)
                continue;
            if (metrics != null) metrics.Expansions++;
            current.InClosed = true;
            if (current.Position.Equals(end))
                return current;
            foreach (var offset in diag ? DiagMovePoints : NoDiagMovePoints)
            {
                var to = current.Position + offset;
                if (metrics != null) metrics.CanStepCalls++;
                if (!map.IsValidStep(current.Position, to, to.Equals(end), user.AllowOverride, false, user)
                    || admit != null && !admit(current.Position, to, to.Equals(end)))
                    continue;
                var node = nodes[to.X, to.Y] ??= new PathFinderNode(to);
                var cost = current.Cost + 1;
                if (node.InClosed || cost >= node.Cost)
                    continue;
                node.Cost = cost;
                node.Next = current;
                Enqueue(node);
            }
        }
        return null;

        void Enqueue(PathFinderNode node)
        {
            var heuristic = Math.Max(Math.Abs(node.Position.X - end.X), Math.Abs(node.Position.Y - end.Y));
            if (metrics != null) metrics.HeapOperations++;
            open.Enqueue((node, node.Cost), (node.Cost + heuristic, node.Position.GetDistanceSquared(end), order++));
        }
    }
}
