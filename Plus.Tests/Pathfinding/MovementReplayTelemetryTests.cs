using System.Globalization;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("already there")]
    [InlineData("normal")]
    [InlineData("fast")]
    [InlineData("superfast")]
    [InlineData("replacement")]
    [InlineData("sealed corner")]
    [InlineData("locked pending")]
    [InlineData("cancel")]
    [InlineData("walk magic")]
    [InlineData("magic height")]
    public void ExecutorReplayTimingClassificationRequiresAccuratePacketTelemetry(string scenario)
    {
        var inputs = RecordMovementScenario(scenario);
        var legacy = ReplayMovement(inputs, PathfindingEngine.Legacy);
        var v2 = ReplayMovement(inputs, PathfindingEngine.V2);

        for (var tick = 0; tick < legacy.Count; tick++)
        {
            Assert.True(ReplayTelemetryMatches(legacy[tick]), $"legacy telemetry at tick {tick}");
            Assert.True(ReplayTelemetryMatches(v2[tick]), $"v2 telemetry at tick {tick}");

            if (FrameBytes(legacy[tick]) != FrameBytes(v2[tick]))
            {
                Assert.True(ReplayTimingDifference(legacy[tick], v2[tick]) || ReplayTrajectoryClass(scenario) != null,
                    $"classified telemetry at tick {tick}");
            }
        }
    }

    [Fact]
    public void ExecutorReplayTimingClassifierAcceptsSameTickMvRemovalAtTheSamePositionAndRotation()
    {
        var legacy = ReplayTelemetryFrame(new(1, 1, 1, "0", 2, 2, "/mv 2,1,0//"), "mv 2,1,0");
        var v2 = ReplayTelemetryFrame(new(1, 1, 1, "0", 2, 2, "//"), "");
        Assert.True(ReplayTimingDifference(legacy, v2));
    }

    [Theory]
    [InlineData("actor identity")]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("z")]
    [InlineData("head")]
    [InlineData("rotation")]
    [InlineData("posture")]
    public void ExecutorReplayTimingClassifierRejectsMalformedOrDifferentNonMovementTelemetry(string mutation)
    {
        var baseline = new ReplayStatus(1, 1, 1, "0", 2, 2, "//");
        var changed = mutation switch
        {
            "actor identity" => baseline with { VirtualId = 99 },
            "x" => baseline with { X = 9 },
            "y" => baseline with { Y = 9 },
            "z" => baseline with { Z = "9" },
            "head" => baseline with { Head = 6 },
            "rotation" => baseline with { Rotation = 6 },
            "posture" => baseline with { Status = "/sit .5//" },
            _ => throw new InvalidOperationException(mutation)
        };
        Assert.False(ReplayTimingDifference(ReplayTelemetryFrame(baseline, ""), ReplayTelemetryFrame(changed, "")));
    }

    private static bool ReplayTimingDifference(ReplayFrame legacy, ReplayFrame v2)
    {
        if (legacy.Location != v2.Location || ReplayNonMovementStatus(legacy.Status) != ReplayNonMovementStatus(v2.Status))
        {
            return false;
        }

        if (!ReplayTelemetryMatches(legacy) || !ReplayTelemetryMatches(v2))
        {
            return false;
        }

        var facing = legacy.Packets.Select(packet => (packet.Head, packet.Rotation)).Distinct().ToArray();
        var other = v2.Packets.Select(packet => (packet.Head, packet.Rotation)).Distinct().ToArray();

        return facing.Length == 0 || other.Length == 0 || facing.SequenceEqual(other);
    }

    private static bool ReplayTelemetryMatches(ReplayFrame frame)
    {
        foreach (var packet in frame.Packets)
        {
            if (packet.VirtualId != 1 || packet.X != frame.Location.X || packet.Y != frame.Location.Y)
            {
                return false;
            }

            if (!double.TryParse(packet.Z, NumberStyles.Float, CultureInfo.InvariantCulture, out var z) || z != frame.Location.Z)
            {
                return false;
            }

            if (ReplayNonMovementStatus(packet.Status) != ReplayNonMovementStatus(frame.Status))
            {
                return false;
            }
        }

        return true;
    }

    private static string ReplayNonMovementStatus(string status) => string.Join('/', status
        .Split('/', StringSplitOptions.RemoveEmptyEntries).Where(part => !part.StartsWith("mv ")).Order());
    private static ReplayFrame ReplayTelemetryFrame(ReplayStatus packet, string status)
        => new((1, 1, 0d), status, [], [packet]);
}
