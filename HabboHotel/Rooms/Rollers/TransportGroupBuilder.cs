namespace Plus.HabboHotel.Rooms.Rollers;

// Loads that follow each other's departure form one chain segment; a rotating loop is one group.
internal sealed class TransportGroupBuilder
{
    internal IReadOnlyList<TransportGroup> Build(TransportResolution resolution)
    {
        var segments = new Dictionary<RollerLoad, List<RollerMove>>(ReferenceEqualityComparer.Instance);
        foreach (var (load, moves) in resolution.Departing)
        {
            if (moves.Count == 0 || resolution.LoopTiles.Contains(load.Origin)) continue;
            var head = Head(load, resolution);
            if (!segments.TryGetValue(head, out var segment)) segments[head] = segment = new();
            segment.AddRange(moves);
        }
        return resolution.Loops
            .Concat(segments.Values.Select(Segment))
            .OrderBy(group => group.FirstRollerId).ToList();
    }

    private static RollerLoad Head(RollerLoad load, TransportResolution resolution)
    {
        while (resolution.Follows.TryGetValue(load, out var downstream)) load = downstream;
        return load;
    }

    private static TransportGroup Segment(List<RollerMove> moves)
        => new(moves.Select(move => move.Roller).Distinct().Count() == 1 ? TransportGroupKind.Single : TransportGroupKind.Chain, moves);
}
