using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class ValidPrefixFinderTests
{
    private static readonly PrefixCandidate Origin = new(0, 0, 0, 0, 0);

    [Fact]
    public void SingleSurfaceTilesWalkTheWholeRouteWhenEveryEdgeIsValid()
    {
        var graph = new LayeredGraph().Floor(1, 0).Floor(2, 0).Floor(3, 0);
        var prefix = Find(graph, Origin, Step(1), Step(2), Goal(3));
        Assert.Equal(new[] { 1, 2, 3 }, prefix.Select(c => c.X).ToArray());
    }

    [Fact]
    public void FrontierStopsAtTheFirstUnreachableIndexAndKeepsTheFarthestReachablePrefix()
    {
        var graph = new LayeredGraph().Floor(1, 0).Floor(2, 0).Floor(3, 0).Floor(4, 0);
        graph.Denied.Add((3, 0));
        var prefix = Find(graph, Origin, Step(1), Step(2), Step(3), Goal(4));
        Assert.Equal(new[] { 1, 2 }, prefix.Select(c => c.X).ToArray());
    }

    [Fact]
    public void MissingTileOrBlockedFirstEdgeYieldsAnEmptyPrefix()
    {
        var graph = new LayeredGraph().Floor(2, 0);
        Assert.Empty(Find(graph, Origin, Step(1), Goal(2)));
        Assert.Empty(Find(graph, Origin));
    }

    [Fact]
    public void LoweredSupportContinuesThroughTheSecondSurfaceToTheHigherTile()
    {
        // Current Z 2; the original support (7) is now at 0.1, another surface at 1.7; next tile at 3.
        var graph = new LayeredGraph()
            .Surface(1, 0, z: 0.1, support: 7).Surface(1, 0, z: 1.7, support: 8)
            .Surface(2, 0, z: 3, support: 9);
        var start = new PrefixCandidate(0, 0, 2, 5, 0);
        var prefix = Find(graph, start, new RetainedStep(1, 0, StepPurpose.Transit, 7, 2),
            new RetainedStep(2, 0, StepPurpose.Goal, 9, 3));
        Assert.Equal(new[] { (1, 1.7), (2, 3d) }, prefix.Select(c => (c.X, c.Z)).ToArray());
    }

    [Fact]
    public void NearerButDeniedCandidateFallsBackToTheValidSurfaceOnTheSameTile()
    {
        var graph = new LayeredGraph()
            .Surface(1, 0, z: 0, support: 7).Surface(1, 0, z: 1, support: 8)
            .Surface(2, 0, z: 1, support: 0);
        graph.DeniedSurfaces.Add((1, 7));
        var prefix = Find(graph, Origin, new RetainedStep(1, 0, StepPurpose.Transit, 7, 0), Goal(2, z: 1));
        Assert.Equal(new[] { 8u, 0u }, prefix.Select(c => c.SupportItem).ToArray());
    }

    [Fact]
    public void AdvisoryMetadataBreaksTiesBySupportThenNearestZThenLowerSlot()
    {
        var bySupport = new LayeredGraph().Surface(1, 0, z: 0.5, support: 3).Surface(1, 0, z: 1, support: 4);
        Assert.Equal(4u, Find(bySupport, Origin, new RetainedStep(1, 0, StepPurpose.Goal, 4, 0.5)).Single().SupportItem);
        var byZ = new LayeredGraph().Surface(1, 0, z: 0.2, support: 3).Surface(1, 0, z: 1, support: 4);
        Assert.Equal(1d, Find(byZ, Origin, new RetainedStep(1, 0, StepPurpose.Goal, 9, 0.9)).Single().Z);
        var bySlot = new LayeredGraph().Surface(1, 0, z: 0, support: 3).Surface(1, 0, z: 1, support: 4);
        Assert.Equal(0, Find(bySlot, Origin, new RetainedStep(1, 0, StepPurpose.Goal, 9, 0.5)).Single().Slot);
    }

    [Fact]
    public void DisappearedSupportReferenceIsNeverItselfABlockage()
    {
        // The retained step remembers a rug (support 9); only floor remains on that tile.
        var graph = new LayeredGraph().Floor(1, 0).Floor(2, 0);
        var prefix = Find(graph, Origin, new RetainedStep(1, 0, StepPurpose.Transit, 9, 0), Goal(2));
        Assert.Equal(new[] { 1, 2 }, prefix.Select(c => c.X).ToArray());
    }

    [Fact]
    public void EachEdgeIsCheckedWithItsRetainedPurpose()
    {
        var graph = new LayeredGraph().Floor(1, 0).Floor(2, 0);
        Find(graph, Origin, Step(1), Goal(2));
        Assert.Equal(new[] { (1, StepPurpose.Transit), (2, StepPurpose.Goal) },
            graph.Checked.Distinct().ToArray());
    }

    [Fact]
    public void BackpointersFollowAReachablePredecessorRatherThanTheBestRankedOne()
    {
        // The preferred surface on tile 1 cannot climb to tile 2; the other one can.
        var graph = new LayeredGraph()
            .Surface(1, 0, z: 0, support: 7).Surface(1, 0, z: 1, support: 8)
            .Surface(2, 0, z: 2.4, support: 0);
        var prefix = Find(graph, Origin, new RetainedStep(1, 0, StepPurpose.Transit, 7, 0), Goal(2, z: 2.4));
        Assert.Equal(new[] { 8u, 0u }, prefix.Select(c => c.SupportItem).ToArray());
    }

    private static PrefixCandidate[] Find(LayeredGraph graph, PrefixCandidate start, params RetainedStep[] steps)
        => new ValidPrefixFinder().Find(start, steps, graph);

    private static RetainedStep Step(int x, double z = 0) => new(x, 0, StepPurpose.Transit, 0, z);
    private static RetainedStep Goal(int x, double z = 0) => new(x, 0, StepPurpose.Goal, 0, z);

    private sealed class LayeredGraph : IPrefixGraph
    {
        private readonly Dictionary<(int X, int Y), List<PrefixCandidate>> _tiles = new();
        public HashSet<(int X, int Y)> Denied { get; } = new();
        public HashSet<(int X, uint Support)> DeniedSurfaces { get; } = new();
        public List<(int X, StepPurpose Purpose)> Checked { get; } = new();

        public LayeredGraph Floor(int x, int y) => Surface(x, y, 0, 0);

        public LayeredGraph Surface(int x, int y, double z, uint support)
        {
            if (!_tiles.TryGetValue((x, y), out var list)) _tiles[(x, y)] = list = new();
            list.Add(new(x, y, z, support, list.Count));
            return this;
        }

        public int Candidates(int x, int y, Span<PrefixCandidate> into)
        {
            if (!_tiles.TryGetValue((x, y), out var list)) return 0;
            for (var i = 0; i < list.Count; i++) into[i] = list[i];
            return list.Count;
        }

        public bool CanStep(in PrefixCandidate from, in PrefixCandidate to, StepPurpose purpose)
        {
            Checked.Add((to.X, purpose));
            return Math.Abs(to.X - from.X) <= 1 && Math.Abs(to.Y - from.Y) <= 1
                && !Denied.Contains((to.X, to.Y)) && !DeniedSurfaces.Contains((to.X, to.SupportItem))
                && to.Z - from.Z <= 1.5;
        }
    }
}
