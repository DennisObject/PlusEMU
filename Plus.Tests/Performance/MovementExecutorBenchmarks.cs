using System.Diagnostics;
using System.Reflection;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Performance;

public class MovementExecutorBenchmarks
{
    [Fact]
    public void MeasureMovementPhasesAndSharedTargetSearchBatches()
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_EXECUTOR_BENCHMARK");

        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        var reports = new List<string>
        {
            $"Runtime={Environment.Version}; Release/AnyCPU; warmed; no database/sockets; 100 actors; 64²",
            "Movement includes intake/commit/search scheduler/announce and landing hooks; excludes path setup, AI and status serialization.",
            "Shared-target workloads are synthetic equal-distance search batches; no flow-field caching is implemented."
        };
        MeasureMovement(PathfindingEngine.Legacy, reports);
        MeasureMovement(PathfindingEngine.V2, reports);
        MeasureTargets(shared: true, reports);
        MeasureTargets(shared: false, reports);
        File.WriteAllLines(output, reports);
    }

    private static void MeasureMovement(PathfindingEngine engine, List<string> reports)
    {
        var fixture = new MovementBenchmarkFixture(engine);
        var samples = new double[600];

        for (var warmup = 0; warmup < 30; warmup++)
        {
            fixture.Prepare();
            fixture.Tick();
            fixture.Tick();
        }

        long allocated = 0;

        for (var pair = 0; pair < samples.Length / 2; pair++)
        {
            fixture.Prepare();
            var bytes = GC.GetAllocatedBytesForCurrentThread();

            for (var phase = 0; phase < 2; phase++)
            {
                var start = Stopwatch.GetTimestamp();
                fixture.Tick();
                samples[pair * 2 + phase] = Microseconds(start);
            }

            allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            fixture.VerifyLanding();
        }

        reports.Add(Report($"100 {engine} walkers movement phases, target <1000µs/tick", samples, allocated / samples.Length));
    }

    private static void MeasureTargets(bool shared, List<string> reports)
    {
        var corpus = TargetCorpus(shared);
        var settings = new PathfindingSettings();
        var search = new PathSearch(corpus.Grid, settings);
        var route = new Route();
        using var lease = PathWorkspacePool.Rent(corpus.Grid.SlotCapacity, corpus.Grid.ActiveNodeCount);
        var workspace = lease.Workspace;

        for (var warmup = 0; warmup < 20; warmup++)
        {
            SearchBatch(search, corpus.Requests, workspace, route);
        }

        var samples = new double[200];
        var counters = new SearchBatchCounters();
        var bytes = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < samples.Length; index++)
        {
            var start = Stopwatch.GetTimestamp();
            counters = SearchBatch(search, corpus.Requests, workspace, route);
            samples[index] = Microseconds(start);
        }

        bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / samples.Length;
        Assert.Equal(100, counters.Found);
        reports.Add(Report($"100 v2 searches, {(shared ? "one shared" : "100 independent")} target(s)", samples, bytes)
            + $"; expansions={counters.Expansions}; CanStep={counters.CanStep}; heap_ops={counters.HeapOperations}; total_steps={counters.Steps}");
        reports.Add($"  retained grid+connectivity={corpus.Grid.RetainedBytes}B; workspace={workspace.RetainedBytes}B");
    }

    private static (NavGrid Grid, SearchRequest[] Requests) TargetCorpus(bool shared)
    {
        var (grid, _, _) = Plus.Tests.Pathfinding.NavTest.Create(64, 64);
        var actor = new ActorProfile { IgnoreUsers = true };
        var requests = new SearchRequest[100];

        for (var index = 0; index < requests.Length; index++)
        {
            var offset = index % 25;
            var side = index / 25;
            var (dx, dy) = side switch
            {
                0 => (-30, offset - 12),
                1 => (30, offset - 12),
                2 => (offset - 12, -30),
                _ => (offset - 12, 30)
            };
            var goalX = shared ? 32 : 15 + index % 25;
            var goalY = shared ? 32 : 15 + index / 25;
            var fromX = shared ? goalX + dx : goalX;
            var fromY = shared ? goalY + dy : goalY + 30;
            requests[index] = new(actor, grid.Position(grid.Tile(fromX, fromY)), goalX, goalY);
        }

        return (grid, requests);
    }

    private static SearchBatchCounters SearchBatch(PathSearch search, SearchRequest[] requests,
        PathWorkspace workspace, Route route)
    {
        var counters = new SearchBatchCounters();

        foreach (var request in requests)
        {
            var outcome = search.Find(request, workspace, route);
            counters = new(counters.Found + (outcome == PathOutcome.Found ? 1 : 0),
                counters.Expansions + workspace.Expansions, counters.CanStep + workspace.CanStepCalls,
                counters.HeapOperations + workspace.HeapOperations, counters.Steps + route.Count);
        }

        return counters;
    }

    private static double Microseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;
    private static string Report(string name, double[] samples, long bytes)
    {
        Array.Sort(samples);
        double Percentile(double fraction) => samples[Math.Clamp((int)Math.Ceiling(samples.Length * fraction) - 1, 0, samples.Length - 1)];

        return $"{name}: p50={Percentile(.5):F2}µs p95={Percentile(.95):F2}µs p99={Percentile(.99):F2}µs allocated={bytes}B/tick-or-batch n={samples.Length}";
    }
    private readonly record struct SearchBatchCounters(int Found = 0, int Expansions = 0, int CanStep = 0, int HeapOperations = 0, int Steps = 0);

    private sealed class MovementBenchmarkFixture
    {
        private readonly RoomPerformanceFixture _fixture = RoomPerformanceFixture.Create(100, 0, 64);
        private readonly PathfindingEngine _engine;
        private readonly RoomNavigation _navigation;
        private readonly int[] _landing = new int[100];

        internal MovementBenchmarkFixture(PathfindingEngine engine)
        {
            _engine = engine;
            RoomPerformanceFixture.SetField(_fixture.Room, "_roomItemHandling", new RoomItemHandling(_fixture.Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
            _fixture.Map.GenerateMaps();
            _navigation = new(_fixture.Room, _fixture.Map.StaticModel, new()
            {
                Engine = engine
            }, TestLogging.Navigation,
                TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_fixture.Map, _navigation);
            _navigation.Compiler.RebuildAll();
            InitializeActors();
        }

        private void InitializeActors()
        {
            using var owner = _engine == PathfindingEngine.V2 ? RoomOwnerScope.Enter(_fixture.Room) : null;

            for (var index = 0; index < _fixture.Bots.Count; index++)
            {
                var actor = _fixture.Bots[index];
                _fixture.Map.RemoveUserFromMap(actor, actor.Coordinate);
                actor.X = index % 2 == 0 ? 1 : 32;
                actor.Y = index / 2 + 1;
                actor.Statusses.Clear();
                actor.AllowOverride = false;
                actor.Path.Clear();

                if (_engine == PathfindingEngine.V2)
                {
                    _navigation.Admit(actor);
                }
                else
                {
                    _fixture.Map.AddUserToMap(actor, actor.Coordinate);
                }
            }
        }

        internal void Prepare()
        {
            for (var index = 0; index < _fixture.Bots.Count; index++)
            {
                var actor = _fixture.Bots[index];
                var origin = index % 2 == 0 ? 1 : 32;
                _landing[index] = actor.X == origin ? origin + 1 : origin;
                actor.GoalX = _landing[index];
                actor.GoalY = actor.Y;
                actor.IsWalking = true;
                actor.PathRecalcNeeded = false;
                actor.SetStep = false;

                if (_engine == PathfindingEngine.V2)
                {
                    PrepareV2(actor);
                }
                else
                {
                    PrepareLegacy(actor);
                }
            }
        }

        private void PrepareV2(RoomUser actor)
        {
            var state = actor.Movement;
            state.Route.Count = 1;
            state.Route.GridVersion = _navigation.Grid.Version;
            state.Route.Set(0, _navigation.Grid.Reference(_navigation.Grid.Tile(actor.GoalX, actor.GoalY)));
            state.Route.GoalSurface = state.Route.Steps[0];
            state.Cursor = 0;
            state.HasIntent = true;
            state.Origin = MoveOrigin.Bot;
        }

        private static void PrepareLegacy(RoomUser actor)
        {
            actor.Path.Clear();
            actor.Path.Add(new(actor.GoalX, actor.GoalY));
            actor.Path.Add(new(actor.X, actor.Y));
            actor.PathStep = 1;
        }

        internal void Tick()
        {
            using var owner = _engine == PathfindingEngine.V2 ? RoomOwnerScope.Enter(_fixture.Room) : null;
            _navigation.DrainCommands();
            _fixture.Manager.OnCycle();
        }

        internal void VerifyLanding()
        {
            for (var index = 0; index < _fixture.Bots.Count; index++)
            {
                Assert.Equal(_landing[index], _fixture.Bots[index].X);
            }
        }
    }
}
