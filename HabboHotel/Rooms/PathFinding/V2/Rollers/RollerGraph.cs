using System.Drawing;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Roller -> next-roller edges between loaded rollers. Each load has at most one successor.
internal sealed class RollerGraph
{
    private readonly Dictionary<Point, RollerLoad> _byOrigin = new();
    private readonly Dictionary<Point, List<RollerLoad>> _feeders = new();

    internal RollerGraph(IReadOnlyList<RollerLoad> loads)
    {
        Loads = loads;

        foreach (var load in loads) {
            _byOrigin[load.Origin] = load;

            if (!_feeders.TryGetValue(load.Destination, out var feeders)) {
                _feeders[load.Destination] = feeders = new();
            }

            feeders.Add(load);
        }
    }

    internal IReadOnlyList<RollerLoad> Loads { get; }

    internal RollerLoad? At(Point tile) => _byOrigin.GetValueOrDefault(tile);

    internal IReadOnlyList<RollerLoad> FeedersOf(Point tile) => _feeders.TryGetValue(tile, out var feeders) ? feeders : [];

    // Full loops: every roller on the cycle carries a load.
    internal IReadOnlyList<IReadOnlyList<RollerLoad>> Loops()
    {
        var loops = new List<IReadOnlyList<RollerLoad>>();
        var done = new HashSet<RollerLoad>(ReferenceEqualityComparer.Instance);

        foreach (var start in Loads) {
            var path = new List<RollerLoad>();

            for (var load = start; load != null && done.Add(load); load = At(load.Destination)) {
                path.Add(load);
            }

            var last = path.Count == 0 ? null : At(path[^1].Destination);
            var entry = last == null ? -1 : path.IndexOf(last);

            if (entry >= 0) {
                loops.Add(path.GetRange(entry, path.Count - entry));
            }
        }

        return loops;
    }
}
