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
    {
        var path = new List<Vector2D>();
        var nodes = FindPathReversed(user, diag, map, start, end);
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
        Vector2D end)
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
            var current = entry.Node;
            if (current.InClosed || entry.Cost != current.Cost)
                continue;
            current.InClosed = true;
            if (current.Position.Equals(end))
                return current;
            foreach (var offset in diag ? DiagMovePoints : NoDiagMovePoints)
            {
                var to = current.Position + offset;
                if (!map.IsValidStep(current.Position, to, to.Equals(end), user.AllowOverride, false, user))
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
            open.Enqueue((node, node.Cost), (node.Cost + heuristic, node.Position.GetDistanceSquared(end), order++));
        }
    }
}
