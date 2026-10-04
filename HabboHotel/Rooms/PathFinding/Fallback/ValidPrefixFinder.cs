namespace Plus.HabboHotel.Rooms.PathFinding;

public readonly record struct PrefixCandidate(int X, int Y, double Z, uint SupportItem, int Slot);

// Engine adapter: current surfaces on a tile and the full step check under the prefix occupancy view.
public interface IPrefixGraph
{
    int Candidates(int x, int y, Span<PrefixCandidate> into);
    bool CanStep(in PrefixCandidate from, in PrefixCandidate to, StepPurpose purpose);
}

// Longest reachable prefix along a fixed XY sequence; O(length x K^2) with K <= MaxCandidates.
public sealed class ValidPrefixFinder
{
    public const int MaxCandidates = 4;

    public PrefixCandidate[] Find(in PrefixCandidate start, IReadOnlyList<RetainedStep> steps, IPrefixGraph graph)
    {
        var layers = new Layer[steps.Count];
        var previous = new Layer([start], [-1]);
        var reached = 0;
        while (reached < steps.Count)
        {
            var layer = Expand(previous, steps[reached], graph);
            if (layer.Candidates.Length == 0) break;
            layers[reached++] = previous = layer;
        }
        return Reconstruct(layers, reached);
    }

    private static Layer Expand(Layer previous, in RetainedStep step, IPrefixGraph graph)
    {
        Span<PrefixCandidate> buffer = stackalloc PrefixCandidate[MaxCandidates];
        var count = Math.Min(graph.Candidates(step.X, step.Y, buffer), MaxCandidates);
        var kept = new List<(PrefixCandidate Candidate, int Parent)>(count);
        for (var i = 0; i < count; i++)
        {
            var parent = FirstPredecessor(previous, buffer[i], step.Purpose, graph);
            if (parent >= 0) kept.Add((buffer[i], parent));
        }
        var advisory = step;
        kept.Sort((a, b) => Rank(a.Candidate, advisory).CompareTo(Rank(b.Candidate, advisory)));
        return new(kept.Select(k => k.Candidate).ToArray(), kept.Select(k => k.Parent).ToArray());
    }

    // Layers are ranked, so the first reachable predecessor is the closest to its own advisory data.
    private static int FirstPredecessor(Layer previous, in PrefixCandidate to, StepPurpose purpose, IPrefixGraph graph)
    {
        for (var p = 0; p < previous.Candidates.Length; p++)
            if (graph.CanStep(previous.Candidates[p], to, purpose)) return p;
        return -1;
    }

    // Metadata only breaks ties: same support item, then nearest original Z, then lower slot.
    private static (int, double, int) Rank(in PrefixCandidate candidate, in RetainedStep advisory)
        => (candidate.SupportItem == advisory.SupportItem ? 0 : 1, Math.Abs(candidate.Z - advisory.Z), candidate.Slot);

    private static PrefixCandidate[] Reconstruct(Layer[] layers, int reached)
    {
        var prefix = new PrefixCandidate[reached];
        var index = 0;
        for (var i = reached - 1; i >= 0; i--)
        {
            prefix[i] = layers[i].Candidates[index];
            index = layers[i].Parents[index];
        }
        return prefix;
    }

    private sealed record Layer(PrefixCandidate[] Candidates, int[] Parents);
}
