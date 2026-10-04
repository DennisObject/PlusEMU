namespace Plus.HabboHotel.Rooms.PathFinding;

// Loads that follow each other's departure form one chain segment; a rotating loop is one group.
internal sealed class TransportGroupBuilder
{
    internal IReadOnlyList<TransportGroup> Build(TransportResolution resolution)
    {
        var heads = new Dictionary<RollerLoad, RollerLoad>(ReferenceEqualityComparer.Instance);
        var segments = new Dictionary<RollerLoad, List<RollerMove>>(ReferenceEqualityComparer.Instance);
        foreach (var (load, moves) in resolution.Departing)
        {
            if (moves.Count == 0 || resolution.LoopTiles.Contains(load.Origin)) continue;
            var head = Head(load, resolution, heads);
            if (!segments.TryGetValue(head, out var segment)) segments[head] = segment = new();
            segment.AddRange(moves);
        }
        return resolution.Loops
            .Concat(segments.Values.Select(Segment))
            .OrderBy(group => group.FirstRollerId).ToList();
    }

    // Memoized with path compression, so a long chain is walked once.
    private static RollerLoad Head(RollerLoad load, TransportResolution resolution, Dictionary<RollerLoad, RollerLoad> heads)
    {
        var path = new List<RollerLoad>();
        var current = load;
        RollerLoad? head = null;
        while (head == null)
        {
            if (heads.TryGetValue(current, out var known)) head = known;
            else if (resolution.Follows.TryGetValue(current, out var downstream)) { path.Add(current); current = downstream; }
            else head = current;
        }
        foreach (var visited in path) heads[visited] = head;
        return head;
    }

    private static TransportGroup Segment(List<RollerMove> moves)
        => new(moves.Select(move => move.Roller).Distinct().Count() == 1 ? TransportGroupKind.Single : TransportGroupKind.Chain, moves);
}
