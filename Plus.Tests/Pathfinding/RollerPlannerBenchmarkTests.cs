using System.Diagnostics;
using System.Drawing;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms.Rollers;
using Xunit;

namespace Plus.Tests;

// Opt-in scaling data for the roller planner: set PLUSEMU_ROLLER_BENCHMARK to an output file.
public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(PathfindingEngine.Legacy, false, 32)]
    [InlineData(PathfindingEngine.Legacy, false, 64)]
    [InlineData(PathfindingEngine.Legacy, false, 128)]
    [InlineData(PathfindingEngine.Legacy, false, 250)]
    [InlineData(PathfindingEngine.Legacy, true, 8)]
    [InlineData(PathfindingEngine.Legacy, true, 16)]
    [InlineData(PathfindingEngine.Legacy, true, 32)]
    [InlineData(PathfindingEngine.Legacy, true, 64)]
    [InlineData(PathfindingEngine.V2, false, 32)]
    [InlineData(PathfindingEngine.V2, false, 64)]
    [InlineData(PathfindingEngine.V2, false, 128)]
    [InlineData(PathfindingEngine.V2, false, 250)]
    [InlineData(PathfindingEngine.V2, true, 8)]
    [InlineData(PathfindingEngine.V2, true, 16)]
    [InlineData(PathfindingEngine.V2, true, 32)]
    [InlineData(PathfindingEngine.V2, true, 64)]
    public void MeasureRollerPlannerScaling(PathfindingEngine engine, bool loop, int size)
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_ROLLER_BENCHMARK");
        if (string.IsNullOrEmpty(output)) return;
        var tiles = loop ? BenchmarkLoop(size) : BenchmarkChain(size);
        InstallRollerChainEngine(engine);
        PlannerActor(1, 0, 0, 0);
        ExecutorTick(); ExecutorTick();
        var planning = MinimumPlanningMicroseconds(engine);
        EnableExecutorRollers(); ExecutorTick();
        var cycle = MedianTickMicroseconds();
        lock (typeof(PlacedFurniRoomTests))
            File.AppendAllLines(output, [FormattableString.Invariant(
                $"{engine} {(loop ? "full loop" : "loaded chain")} rollers={tiles} plan_us={planning:F0} plan_us_per_roller={planning / tiles:F2} cycle_median_us={cycle:F0}")]);
    }

    // Snapshot and planning are pure, so they can be repeated on the same room state.
    private double MinimumPlanningMicroseconds(PathfindingEngine engine)
    {
        IRollerTransportEngine transport = engine == PathfindingEngine.V2
            ? new RollerTransport(_room.GetGameMap().Navigation!, _room.GetGameMap().Navigation!.Executor.Context,
                new(_room, _room.GetGameMap().Navigation!, _room.GetGameMap().Navigation!.Executor.Context,
                    new(_room.GetGameMap().Navigation!.Executor.Context)))
            : new LegacyRollerTransport(_room, _room.GetRoomItemHandler());
        var loads = new RollerLoadBuilder(_room, transport);
        var planner = new RollerTransportPlanner(new(new RollerAdmission(_room, transport)), new());
        var rollers = _room.GetRoomItemHandler().GetRollers().ToList();
        var best = double.MaxValue;
        for (var repetition = 0; repetition < 60; repetition++)
        {
            var start = Stopwatch.GetTimestamp();
            Assert.NotEmpty(planner.Plan(loads.Build(rollers)));
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMicroseconds);
        }
        return best;
    }

    private double MedianTickMicroseconds()
    {
        var samples = new double[15];
        for (var cycle = 0; cycle < samples.Length; cycle++)
        {
            var start = Stopwatch.GetTimestamp();
            ExecutorTick();
            samples[cycle] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }
        return samples.Order().ElementAt(samples.Length / 2);
    }

    // A fully loaded chain along row 1 whose head exits onto the floor: one group of every roller.
    private int BenchmarkChain(int length)
    {
        PrepareRollerChain(length + 1, false);
        for (var x = 0; x < length; x++) PlannerCargo((uint)(1000 + x), x, 1);
        return length;
    }

    // A fully loaded square loop of the given side: one rotation group of every roller.
    private int BenchmarkLoop(int side)
    {
        var rows = string.Join('\r', Enumerable.Repeat(new string('0', side + 2), side + 2));
        Set("_gamemap", new Gamemap(_room, new RoomModel("roller-loop", 0, 0, 0, 0, rows, false, 0, false)));
        _room.GetGameMap().GenerateMaps();
        var perimeter = LoopPerimeter(side).ToList();
        for (var index = 0; index < perimeter.Count; index++)
        {
            var (tile, rotation) = perimeter[index];
            PlannerRoller((uint)(10 + index), tile.X, tile.Y, rotation);
            PlannerCargo((uint)(5000 + index), tile.X, tile.Y);
        }
        return perimeter.Count;
    }

    private static IEnumerable<(Point Tile, int Rotation)> LoopPerimeter(int side)
    {
        for (var x = 1; x < side; x++) yield return (new(x, 1), 2);
        for (var y = 1; y < side; y++) yield return (new(side, y), 4);
        for (var x = side; x > 1; x--) yield return (new(x, side), 6);
        for (var y = side; y > 1; y--) yield return (new(1, y), 0);
    }
}
