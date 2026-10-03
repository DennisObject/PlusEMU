using System.Drawing;

namespace Plus.HabboHotel.Rooms.Rollers;

internal sealed class TransportResolution
{
    internal Dictionary<RollerLoad, IReadOnlyList<RollerMove>> Departing { get; } = new(ReferenceEqualityComparer.Instance);
    // Upstream load -> the downstream load whose departure admitted it.
    internal Dictionary<RollerLoad, RollerLoad> Follows { get; } = new(ReferenceEqualityComparer.Instance);
    internal List<TransportGroup> Loops { get; } = new();
    internal HashSet<Point> LoopTiles { get; } = new();

    internal IReadOnlyList<RollerMove> DepartingFrom(RollerLoad? load)
        => load != null && Departing.TryGetValue(load, out var moves) ? moves : [];
}

// Feasibility before tie-break: full loops reserve their own permutation, then every other
// destination is resolved downstream-first. Cargo stops blocking only once its departure succeeded.
internal sealed class TransportFeasibilityResolver(IRollerAdmission admission)
{
    internal TransportResolution Resolve(RollerGraph graph)
    {
        var resolution = new TransportResolution();
        foreach (var loop in graph.Loops()) ResolveLoop(loop, resolution);
        var ready = new Queue<Point>(graph.Loads.Select(load => load.Destination).Distinct()
            .Where(tile => graph.At(tile) == null || resolution.LoopTiles.Contains(tile)));
        while (ready.TryDequeue(out var tile))
        {
            var feeders = graph.FeedersOf(tile).Where(load => !resolution.LoopTiles.Contains(load.Origin)).ToList();
            ResolveFeeders(tile, feeders, graph.At(tile), resolution);
            foreach (var feeder in feeders) ready.Enqueue(feeder.Origin);
        }
        return resolution;
    }

    private void ResolveLoop(IReadOnlyList<RollerLoad> loop, TransportResolution resolution)
    {
        var rotation = new TransportGroup(TransportGroupKind.Loop, loop.SelectMany(load => load.Moves).ToList());
        var rotates = rotation.Moves.All(move => admission.Admits(move, rotation.DeparturesFrom(move.Destination)));
        foreach (var load in loop)
        {
            resolution.Departing[load] = rotates ? load.Moves : [];
            resolution.LoopTiles.Add(load.Origin);
        }
        if (rotates) resolution.Loops.Add(rotation);
    }

    // A loop tile never has a real vacancy for an external feeder: it either rotates full or stays.
    private void ResolveFeeders(Point tile, IReadOnlyList<RollerLoad> feeders, RollerLoad? standing,
        TransportResolution resolution)
    {
        var departing = RollerDepartures.Of(resolution.DepartingFrom(standing));
        var winner = resolution.LoopTiles.Contains(tile) ? default : feeders
            .Select(load => (Load: load, Admitted: load.Moves.Where(move => admission.Admits(move, departing)).ToList()))
            .Where(candidate => candidate.Admitted.Count > 0)
            .OrderBy(candidate => candidate.Load.Roller.Id)
            .FirstOrDefault();
        foreach (var feeder in feeders)
            resolution.Departing[feeder] = ReferenceEquals(feeder, winner.Load) ? winner.Admitted : [];
        if (winner.Load != null && standing != null && !departing.IsEmpty) resolution.Follows[winner.Load] = standing;
    }
}
