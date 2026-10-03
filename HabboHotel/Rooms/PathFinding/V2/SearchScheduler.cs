namespace Plus.HabboHotel.Rooms.PathFinding;

public readonly record struct SearchJob<TActor>(TActor Actor, long LifetimeId, long GoalRevision)
    where TActor : class;

public readonly record struct SearchResult(PathOutcome Outcome, int Expansions);

// The room owner mutates this queue. Concurrent producers use the room command queue.
public sealed class SearchScheduler<TActor> where TActor : class
{
    private readonly LinkedList<SearchJob<TActor>> _jobs = new();
    private readonly Dictionary<TActor, LinkedListNode<SearchJob<TActor>>> _actors
        = new(ReferenceEqualityComparer.Instance);

    public void Enqueue(TActor actor, long lifetimeId, long goalRevision)
    {
        Remove(actor);
        _actors.Add(actor, _jobs.AddLast(new SearchJob<TActor>(actor, lifetimeId, goalRevision)));
    }

    public void Remove(TActor actor)
    {
        if (_actors.Remove(actor, out var node)) _jobs.Remove(node);
    }

    public int Run(int budget, Func<SearchJob<TActor>, bool> isCurrent,
        Func<SearchJob<TActor>, SearchResult> search, Action<SearchJob<TActor>, SearchResult> onResult)
    {
        var spent = 0;
        while (spent < budget && _jobs.First is { } node)
        {
            var job = node.Value;
            Remove(job.Actor);
            if (!isCurrent(job)) continue;
            var result = search(job);
            spent += result.Expansions;
            if (isCurrent(job)) onResult(job, result);
        }
        return spent;
    }
}
