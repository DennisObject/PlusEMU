using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Performance;

public class PathfindingBenchmarks
{
    [Fact]
    public void MeasureSearchAndLegacyMovement()
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_PATHFINDING_BENCHMARK");
        if (string.IsNullOrEmpty(output)) return;
        var results = new List<string> { $"Runtime={Environment.Version}; Release/AnyCPU; µs/search; no database/sockets; warmed; legacy counters instrumented in benchmark overload" };
        Run("64² open (~30 steps), target p99 <30µs", Open(64), 1, 1, 31, 21, 5000, 500);
        var maze = Open(64);
        for (var x = 8; x < 56; x += 8)
            for (var y = 0; y < 64; y++) if (y != (x / 8 % 2 == 0 ? 20 : 40)) maze[y][x] = 'x';
        Run("64² maze, target p99 <150µs", maze, 1, 30, 60, 30, 1000, 100);
        var cliff = Open(256);
        for (var y = 0; y < 256; y++) cliff[y][128] = '2';
        Run("256² unreachable after prechecks, target p99 <3000µs", cliff, 1, 1, 254, 254, 60, 30);
        var split = Open(64);
        for (var y = 0; y < 64; y++) split[y][32] = 'x';
        Run("64² distinct components, O(1) after warmup", split, 1, 1, 62, 62, 5000, 100);
        var walkers = RoomPerformanceFixture.Create(100, 0, 64);
        var movementSamples = new double[1000];
        for (var warmup = 0; warmup < 30; warmup++) Move();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < movementSamples.Length; i++) { var start = Stopwatch.GetTimestamp(); Move(); movementSamples[i] = Microseconds(start); }
        bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / movementSamples.Length;
        results.Add(Report("100 legacy walkers, two real movement cycles + fixture setup (P2 executor excluded), target <1000µs", movementSamples, bytes));
        File.WriteAllLines(output, results);

        void Move()
        {
            walkers.PrepareNextWalkingStep(); walkers.Manager.OnCycle(); walkers.Manager.OnCycle();
        }

        void Run(string name, char[][] rows, int sx, int sy, int gx, int gy, int v2Iterations, int legacyIterations)
        {
            var model = new RoomModel("benchmark", 0, 0, 0, 0, string.Join('\r', rows.Select(r => new string(r))), 0, 0, false);
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            var map = new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            Set(room, "_gamemap", map); Set(room, "_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems)); Set(room, "_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused));
            map.GenerateMaps();
            var navigation = map.Navigation ?? new RoomNavigation(room, model, new() { Engine = PathfindingEngine.Shadow }, TestLogging.Navigation, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance); navigation.Compiler.RebuildAll();
            var grid = navigation.Grid; var settings = new PathfindingSettings();
            var search = new PathSearch(grid, settings); var route = new Route(); var actor = new ActorProfile { IgnoreUsers = true };
            var legacyActor = new RoomUser(0, 0, 1, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { AllowOverride = false, X = sx, Y = sy };
            var request = new SearchRequest(actor, grid.Position(grid.Tile(sx, sy)), gx, gy);
            using var lease = PathWorkspacePool.Rent(grid.SlotCapacity, grid.ActiveNodeCount);
            var ws = lease.Workspace; var metrics = new PathFinderMetrics();
            var from = new Vector2D(sx, sy); var to = new Vector2D(gx, gy);
            for (var i = 0; i < 30; i++) search.Find(request, ws, route);
            for (var i = 0; i < 5; i++) PathFinder.FindPath(legacyActor, true, map, from, to, metrics);
            var oracle = Plus.Tests.Pathfinding.PathSearchTests.Bfs(grid, settings, request);
            Assert.Equal(oracle.Outcome, search.Find(request, ws, route)); Assert.Equal(oracle.Length, route.Count);
            var v2Samples = new double[v2Iterations]; var legacySamples = new double[legacyIterations];
            using var process = Process.GetCurrentProcess();
            var v2CpuStart = process.TotalProcessorTime;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < v2Iterations; i++) { var start = Stopwatch.GetTimestamp(); search.Find(request, ws, route); v2Samples[i] = Microseconds(start); }
            var v2Bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / v2Iterations;
            var v2CpuMean = (process.TotalProcessorTime - v2CpuStart).TotalMicroseconds / v2Iterations;
            var legacyCpuStart = process.TotalProcessorTime;
            allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < legacyIterations; i++) { var start = Stopwatch.GetTimestamp(); PathFinder.FindPath(legacyActor, true, map, from, to, metrics); legacySamples[i] = Microseconds(start); }
            var legacyBytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / legacyIterations;
            var legacyCpuMean = (process.TotalProcessorTime - legacyCpuStart).TotalMicroseconds / legacyIterations;
            results.Add(Report(name + " v2", v2Samples, v2Bytes) + $"; expansions={ws.Expansions}; CanStep={ws.CanStepCalls}; heap_ops={ws.HeapOperations}; route_steps={route.Count}; process_cpu_mean={v2CpuMean:F2}µs");
            results.Add(Report(name + " legacy", legacySamples, legacyBytes) + $"; expansions={metrics.Expansions}; CanStep={metrics.CanStepCalls}; heap_ops={metrics.HeapOperations}; process_cpu_mean={legacyCpuMean:F2}µs");
            results.Add($"  retained array payload: grid+connectivity={grid.RetainedBytes:N0}B; planning occupancy={grid.SlotCapacity:N0}B; workspace={ws.RetainedBytes:N0}B; slots={grid.SlotCapacity}; active_nodes={grid.ActiveNodeCount}");
        }
    }

    [Fact]
    public void MeasureLayeredSearch()
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_PATHFINDING_BENCHMARK");
        if (string.IsNullOrEmpty(output)) return;
        var results = new List<string> { $"Runtime={Environment.Version}; Release/AnyCPU; µs/search; warmed; same records, layering off (K=1) vs on (K=2)" };
        foreach (var layered in new[] { false, true })
        {
            // 64² floor with a zero-height deck at Z 1 over every even column: K=2 doubles those nodes.
            var settings = new PathfindingSettings { LayeringEnabled = layered };
            var (grid, inputs, compiler) = Plus.Tests.Pathfinding.NavTest.Create(64, 64, settings);
            uint id = 1;
            for (var t = 0; t < 64 * 64; t++)
                if (t % 64 % 2 == 0) inputs.Publish(Plus.Tests.Pathfinding.NavTest.Record(id, id++, [t], z: 1));
            compiler.ApplyNow();
            var search = new PathSearch(grid, settings); var route = new Route();
            var request = new SearchRequest(new ActorProfile(), grid.Position(grid.SurfaceAt(grid.Tile(1, 1), 0)), 62, 62);
            using var lease = PathWorkspacePool.Rent(grid.SlotCapacity, grid.ActiveNodeCount);
            var ws = lease.Workspace;
            for (var i = 0; i < 50; i++) Assert.Equal(PathOutcome.Found, search.Find(request, ws, route));
            var samples = new double[2000];
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < samples.Length; i++) { var start = Stopwatch.GetTimestamp(); search.Find(request, ws, route); samples[i] = Microseconds(start); }
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / samples.Length;
            results.Add(Report($"64² decked columns K={(layered ? 2 : 1)}", samples, bytes)
                + $"; expansions={ws.Expansions}; CanStep={ws.CanStepCalls}; heap_ops={ws.HeapOperations}; route_steps={route.Count}");
            results.Add($"  slots={grid.SlotCapacity}; active_nodes={grid.ActiveNodeCount}; grid+connectivity={grid.RetainedBytes:N0}B; workspace={ws.RetainedBytes:N0}B");
        }
        File.WriteAllLines(output + ".layered", results);
    }

    private static char[][] Open(int size) => Enumerable.Range(0, size).Select(_ => new string('0', size).ToCharArray()).ToArray();
    private static double Microseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;
    private static string Report(string name, double[] times, long bytes)
    {
        Array.Sort(times);
        double Percentile(double p) => times[Math.Clamp((int)Math.Ceiling(times.Length * p) - 1, 0, times.Length - 1)];
        return $"{name}: p50={Percentile(0.5):F2}µs p95={Percentile(0.95):F2}µs p99={Percentile(0.99):F2}µs allocated={bytes}B/op n={times.Length}";
    }
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
}
