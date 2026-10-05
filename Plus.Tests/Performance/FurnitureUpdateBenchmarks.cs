using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests.Performance;

// This harness also compiles unchanged on master for a real before/after baseline.
public class FurnitureUpdateBenchmarks
{
    [Fact]
    public void MeasureLegacyFurnitureUpdateCycle()
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_FURNITURE_BENCHMARK");
        if (string.IsNullOrEmpty(output)) return;
        var results = new List<string> { $"Runtime={Environment.Version}; Release/AnyCPU; warmed µs/update cycle; real Item.ProcessUpdates and RoomItemHandling.OnCycle; no users/database/sockets" };
        Run("500 cosmetic Wired flashes", 500, InteractionType.WiredEffect, false);
        Run("100 adjustable-height state updates", 100, InteractionType.None, true);
        File.WriteAllLines(output, results);

        void Run(string label, int count, InteractionType interaction, bool adjustable)
        {
            var fixture = RoomPerformanceFixture.Create(0, 0);
            var handler = fixture.Room.GetRoomItemHandler();
            var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
            var items = Enumerable.Range(1, count).Select(id => new Item
            {
                Id = (uint)id, IsTemporary = true, RoomId = fixture.Room.RoomId,
                Definition = new ItemDefinition { Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Floor,
                    Length = 1, Width = 1, Walkable = true, Height = 0, InteractionType = interaction,
                    AdjustableHeights = adjustable ? [0, 1] : [] },
                ExtraData = new LegacyDataFormat { Data = "0" }
            }).ToArray();
            foreach (var item in items)
            {
                item.Attach(fixture.Room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards); item.GetX = item.GetY = 2; floor[item.Id] = item;
            }
            fixture.Map.GenerateMaps();
            for (var warmup = 0; warmup < 100; warmup++) Cycle();
            var samples = new double[3000];
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime;
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < samples.Length; i++)
            {
                var start = Stopwatch.GetTimestamp(); Cycle();
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }
            bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / samples.Length;
            var cpuMean = (process.TotalProcessorTime - cpuStart).TotalMicroseconds / samples.Length;
            Array.Sort(samples);
            results.Add($"{label}: p50={samples[1499]:F2}µs p95={samples[2849]:F2}µs p99={samples[2969]:F2}µs allocated={bytes}B/op n={samples.Length}; process_cpu_mean={cpuMean:F2}µs");

            void Cycle()
            {
                foreach (var item in items)
                {
                    item.LegacyDataString = item.LegacyDataString == "0" ? "1" : "0";
                    item.UpdateState(false, false); item.RequestUpdate(1, true);
                }
                handler.OnCycle();
            }
        }
    }
}
