using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class SearchSchedulerTests
{
    [Fact]
    public void ReplacementCoalescesOneJobPerActorAndMovesItToTheTail()
    {
        var scheduler = new SearchScheduler<Actor>();
        var first = new Actor(1);
        var second = new Actor(2);
        var third = new Actor(3);
        Enqueue(scheduler, first);
        Enqueue(scheduler, second);
        Enqueue(scheduler, third);

        for (var revision = 2; revision <= 100; revision++) {
            first.GoalRevision = revision;
            Enqueue(scheduler, first);
        }

        var installed = new List<SearchJob<Actor>>();
        var spent = scheduler.Run(100, IsCurrent, _ => new(PathOutcome.Found, 1),
            (job, _) => installed.Add(job));
        Assert.Equal(3, spent);
        Assert.Equal(new[] { 2, 3, 1 }, installed.Select(job => job.Actor.Id));
        Assert.Equal(100, installed[2].GoalRevision);
        Assert.Equal(first.LifetimeId, installed[2].LifetimeId);
    }

    [Fact]
    public void JobsUseActorReferenceIdentityEvenWhenActorsCompareEqual()
    {
        var scheduler = new SearchScheduler<Actor>();
        var first = new Actor(7);
        var second = new Actor(7);
        Assert.Equal(first, second);
        Enqueue(scheduler, first);
        Enqueue(scheduler, second);
        var installed = new List<Actor>();
        scheduler.Run(10, IsCurrent, _ => new(PathOutcome.Found, 1),
            (job, _) => installed.Add(job.Actor));
        Assert.Equal(2, installed.Count);
        Assert.Same(first, installed[0]);
        Assert.Same(second, installed[1]);
    }

    [Fact]
    public void CancelOrRemoveDeletesOnlyTheMatchingActorsQueuedJob()
    {
        var scheduler = new SearchScheduler<Actor>();
        var cancelled = new Actor(1);
        var remaining = new Actor(2);
        Enqueue(scheduler, cancelled);
        Enqueue(scheduler, remaining);
        scheduler.Remove(cancelled);
        scheduler.Remove(cancelled);
        var installed = new List<Actor>();
        var spent = scheduler.Run(10, IsCurrent, _ => new(PathOutcome.Found, 3),
            (job, _) => installed.Add(job.Actor));
        Assert.Same(remaining, Assert.Single(installed));
        Assert.Equal(3, spent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleLifetimeOrGoalIsDiscardedBeforeSearching(bool lifetimeChanged)
    {
        var scheduler = new SearchScheduler<Actor>();
        var stale = new Actor(1);
        var current = new Actor(2);
        Enqueue(scheduler, stale);
        Enqueue(scheduler, current);
        Invalidate(stale, lifetimeChanged);
        var searched = new List<Actor>();
        var installed = new List<Actor>();
        var spent = scheduler.Run(5, IsCurrent, job =>
        {
            searched.Add(job.Actor);

            return new(PathOutcome.Found, 5);
        }, (job, _) => installed.Add(job.Actor));
        Assert.Equal(5, spent);
        Assert.Same(current, Assert.Single(searched));
        Assert.Same(current, Assert.Single(installed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleLifetimeOrGoalAfterSearchCannotInstallItsResult(bool lifetimeChanged)
    {
        var scheduler = new SearchScheduler<Actor>();
        var actor = new Actor(1);
        Enqueue(scheduler, actor);
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var installed = new List<SearchJob<Actor>>();
        var runner = Task.Run(() => scheduler.Run(1, IsCurrent, _ =>
        {
            started.Set();
            Assert.True(finish.Wait(5000));

            return new(PathOutcome.Found, 8);
        }, (job, _) => installed.Add(job)));

        try {
            Assert.True(started.Wait(5000));
            Invalidate(actor, lifetimeChanged);
        }
        finally {
            finish.Set();
        }

        Assert.Equal(8, await runner);
        Assert.Empty(installed);
    }

    [Fact]
    public void AStartedSearchCompletesBeyondTheBudgetAndDefersTheNextJob()
    {
        var scheduler = new SearchScheduler<Actor>();
        var first = new Actor(1);
        var next = new Actor(2);
        Enqueue(scheduler, first);
        Enqueue(scheduler, next);
        var installed = new List<Actor>();
        var spent = scheduler.Run(3, IsCurrent, _ => new(PathOutcome.Found, 100),
            (job, _) => installed.Add(job.Actor));
        Assert.Equal(100, spent);
        Assert.Same(first, Assert.Single(installed));
        installed.Clear();
        Assert.Equal(4, scheduler.Run(3, IsCurrent, _ => new(PathOutcome.Found, 4),
            (job, _) => installed.Add(job.Actor)));
        Assert.Same(next, Assert.Single(installed));
    }

    [Fact]
    public void AnExactBudgetStopsStartingJobsUntilTheNextRun()
    {
        var scheduler = new SearchScheduler<Actor>();
        var first = new Actor(1);
        var second = new Actor(2);
        Enqueue(scheduler, first);
        Enqueue(scheduler, second);
        var installed = new List<Actor>();
        Assert.Equal(10, scheduler.Run(10, IsCurrent, _ => new(PathOutcome.Found, 10),
            (job, _) => installed.Add(job.Actor)));
        Assert.Same(first, Assert.Single(installed));
        installed.Clear();
        Assert.Equal(10, scheduler.Run(10, IsCurrent, _ => new(PathOutcome.Found, 10),
            (job, _) => installed.Add(job.Actor)));
        Assert.Same(second, Assert.Single(installed));
    }

    [Fact]
    public void ZeroBudgetLeavesJobsQueuedWithoutStartingSearches()
    {
        var scheduler = new SearchScheduler<Actor>();
        var actor = new Actor(1);
        Enqueue(scheduler, actor);
        var spent = scheduler.Run(0, IsCurrent,
            _ => throw new InvalidOperationException("Zero budget started a search."),
            (_, _) => Assert.Fail("Zero budget installed a result."));
        Assert.Equal(0, spent);
        var installed = new List<Actor>();
        scheduler.Run(1, IsCurrent, _ => new(PathOutcome.Found, 1),
            (job, _) => installed.Add(job.Actor));
        Assert.Same(actor, Assert.Single(installed));
    }

    [Fact]
    public void BudgetCancelledIsDeliveredDistinctlyAndNeverAutomaticallyRetried()
    {
        var scheduler = new SearchScheduler<Actor>();
        Enqueue(scheduler, new Actor(1));
        var results = new List<SearchResult>();
        Assert.Equal(2, scheduler.Run(10, IsCurrent, _ => new(PathOutcome.BudgetCancelled, 2),
            (_, result) => results.Add(result)));
        Assert.Equal(PathOutcome.BudgetCancelled, Assert.Single(results).Outcome);
        Assert.Equal(0, scheduler.Run(10, IsCurrent,
            _ => throw new InvalidOperationException("Budget-cancelled search was retried."),
            (_, _) => Assert.Fail("Budget-cancelled result was installed twice.")));
    }

    [Theory]
    [InlineData(PathOutcome.Found)]
    [InlineData(PathOutcome.AlreadyThere)]
    [InlineData(PathOutcome.InvalidGoal)]
    [InlineData(PathOutcome.Unreachable)]
    public void SearchResultsPreserveTheirOutcomeAndExpansionCount(PathOutcome outcome)
    {
        var scheduler = new SearchScheduler<Actor>();
        Enqueue(scheduler, new Actor(1));
        var results = new List<SearchResult>();
        var spent = scheduler.Run(1, IsCurrent, _ => new(outcome, 7),
            (_, result) => results.Add(result));
        Assert.Equal(7, spent);
        Assert.Equal(new SearchResult(outcome, 7), Assert.Single(results));
    }

    [Fact]
    public void ANewGoalQueuedDuringSearchSurvivesDiscardingTheStaleResult()
    {
        var scheduler = new SearchScheduler<Actor>();
        var actor = new Actor(1);
        Enqueue(scheduler, actor);
        var installed = new List<SearchJob<Actor>>();
        Assert.Equal(5, scheduler.Run(5, IsCurrent, _ =>
        {
            actor.GoalRevision++;
            Enqueue(scheduler, actor);

            return new(PathOutcome.Found, 5);
        }, (job, _) => installed.Add(job)));
        Assert.Empty(installed);
        Assert.Equal(1, scheduler.Run(1, IsCurrent, _ => new(PathOutcome.Found, 1),
            (job, _) => installed.Add(job)));
        Assert.Equal(2, Assert.Single(installed).GoalRevision);
    }

    private static void Enqueue(SearchScheduler<Actor> scheduler, Actor actor)
        => scheduler.Enqueue(actor, actor.LifetimeId, actor.GoalRevision);

    private static bool IsCurrent(SearchJob<Actor> job)
        => job.Actor.LifetimeId == job.LifetimeId && job.Actor.GoalRevision == job.GoalRevision;

    private static void Invalidate(Actor actor, bool lifetimeChanged)
    {
        if (lifetimeChanged) {
            actor.LifetimeId++;
        }
        else {
            actor.GoalRevision++;
        }
    }

    private sealed class Actor(int id)
    {
        public int Id { get; } = id;
        public long LifetimeId = 1;
        public long GoalRevision = 1;
        public override bool Equals(object? obj) => obj is Actor actor && actor.Id == Id;
        public override int GetHashCode() => Id;
    }
}
