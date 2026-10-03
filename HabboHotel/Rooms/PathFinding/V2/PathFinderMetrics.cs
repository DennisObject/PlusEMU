namespace Plus.HabboHotel.Rooms.PathFinding;

// Only the benchmark overload observes counters; public legacy entry points stay unchanged.
internal sealed class PathFinderMetrics
{
    public int Expansions;
    public int CanStepCalls;
    public int HeapOperations;
}
