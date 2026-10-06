using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("already there", 0, 1, 0d)]
    [InlineData("normal", 3, 1, 0d)]
    [InlineData("fast", 3, 1, 0d)]
    [InlineData("superfast", 3, 1, 0d)]
    [InlineData("replacement", 1, 2, 0d)]
    [InlineData("sealed corner", 0, 1, 0d)]
    [InlineData("locked pending", 0, 1, 0d)]
    [InlineData("cancel", 0, 1, 0d)]
    [InlineData("walk magic", 1, 1, .75)]
    [InlineData("magic height", 1, 1, 2.125)]
    public void ExecutorReplayKeepsShadowPacketsIdenticalAndClassifiesEveryV2Difference(
        string scenario, int finalX, int finalY, double finalZ)
    {
        var stream = RecordMovementScenario(scenario);
        var legacy = ReplayMovement(stream, PathfindingEngine.Legacy);
        var shadow = ReplayMovement(stream, PathfindingEngine.Shadow);
        var v2 = ReplayMovement(stream, PathfindingEngine.V2);
        Assert.Equal(legacy.Select(FrameBytes), shadow.Select(FrameBytes));
        Assert.Equal((finalX, finalY, finalZ), v2[^1].Location);
        var legacyGoal = scenario switch
        {
            "superfast" => (2, 1, 0d), "locked pending" => (1, 1, 0d), "sealed corner" => (1, 2, 0d),
            _ => (finalX, finalY, finalZ)
        };
        Assert.Equal(legacyGoal, legacy[^1].Location);
        var differences = ClassifyReplayDifferences(legacy, v2, ReplayTrajectoryClass(scenario));
        AssertReplayScenarioDifferences(scenario, differences);
        AssertReplayExactBytes(scenario, legacy, v2, differences);
        if (scenario == "sealed corner") AssertReplaySealedCorner(legacy, v2);
        ReportReplay(scenario, stream, differences);
    }

    [Theory]
    [InlineData(true, 0, 0d)]
    [InlineData(false, 1, 1d)]
    public void ExecutorReplayStacktoolCollisionSettingMatchesLegacyPackets(bool collision, int finalX, double finalZ)
    {
        var stream = new ReplayInput[]
        {
            ReplayFurniture(20, 1, 1, .5, .5, InteractionType.None),
            ReplayFurniture(21, 1, 1, 3, 0, InteractionType.Stacktool, false),
            new(ReplayInputKind.Admit), new(ReplayInputKind.Move, 1, 1),
            new(ReplayInputKind.Tick), new(ReplayInputKind.Tick), new(ReplayInputKind.Tick), new(ReplayInputKind.Tick)
        };
        var legacy = ReplayMovement(stream, PathfindingEngine.Legacy, collision);
        var shadow = ReplayMovement(stream, PathfindingEngine.Shadow, collision);
        var v2 = ReplayMovement(stream, PathfindingEngine.V2, collision);
        Assert.Equal(legacy.Select(FrameBytes), shadow.Select(FrameBytes));
        Assert.Equal((finalX, 1, finalZ), legacy[^1].Location);
        Assert.Equal((finalX, 1, finalZ), v2[^1].Location);
        var differences = ClassifyReplayDifferences(legacy, v2);
        Assert.All(differences, difference => Assert.Equal(ReplayDifference.TimingPhaseChange, difference));
        ReportReplay($"stacktool collision={collision}", stream, differences);
    }

    [Theory]
    [InlineData(ReplayDifference.CornerFix, "corner fix")]
    [InlineData(ReplayDifference.ShorterRoute, "shorter route")]
    [InlineData(ReplayDifference.BlockedTileFix, "blocked-tile fix")]
    [InlineData(ReplayDifference.TimingPhaseChange, "timing-phase change")]
    [InlineData(ReplayDifference.StacktoolExclusion, "stacktool-exclusion")]
    [InlineData(ReplayDifference.Other, "other")]
    public void ExecutorReplayClassificationNamesMatchTheContract(ReplayDifference category, string name)
        => Assert.Equal(name, ReplayDifferenceName(category));

    private static IReadOnlyList<ReplayInput> RecordMovementScenario(string scenario)
    {
        var stream = new List<ReplayInput>();
        AddReplayTerrain(stream, scenario);
        stream.Add(new(ReplayInputKind.Admit));
        if (scenario is "fast" or "superfast") stream.Add(new(ReplayInputKind.Speed, Value: scenario == "fast" ? 2 : 3));
        stream.Add(ReplayScenarioGoal(scenario));
        stream.Add(new(ReplayInputKind.Tick));
        if (scenario == "replacement") stream.Add(new(ReplayInputKind.Move, 1, 2));
        if (scenario == "locked pending") stream.Add(new(ReplayInputKind.Lock, 1, 1));
        if (scenario == "cancel") stream.Add(new(ReplayInputKind.Cancel));
        if (scenario == "magic height")
        {
            stream.Add(new(ReplayInputKind.Tick));
            stream.Add(new(ReplayInputKind.Height, 1, 1, 2.125, ItemId: 11));
        }
        for (var tick = 0; tick < 7; tick++) stream.Add(new(ReplayInputKind.Tick));
        return stream.AsReadOnly();
    }

    private static void AddReplayTerrain(List<ReplayInput> stream, string scenario)
    {
        if (scenario is "walk magic" or "magic height")
        {
            stream.Add(ReplayFurniture(10, 1, 1, 5, 1, InteractionType.None, false));
            stream.Add(ReplayFurniture(11, 1, 1, .75, 0, InteractionType.WalkMagicTile));
        }
        if (scenario != "sealed corner") return;
        stream.Add(ReplayFurniture(10, 1, 1, 0, 1, InteractionType.None, false));
        stream.Add(ReplayFurniture(11, 0, 2, 0, 1, InteractionType.None, false));
    }

    private static ReplayInput ReplayScenarioGoal(string scenario) => scenario switch
    {
        "sealed corner" => new(ReplayInputKind.Move, 1, 2),
        "already there" => new(ReplayInputKind.Move, 0, 1),
        "walk magic" or "magic height" or "locked pending" => new(ReplayInputKind.Move, 1, 1),
        _ => new(ReplayInputKind.Move, 3, 1)
    };

    private static ReplayDifference? ReplayTrajectoryClass(string scenario) => scenario switch
    {
        "sealed corner" => ReplayDifference.CornerFix,
        "locked pending" => ReplayDifference.BlockedTileFix,
        // Original superfast-only walking loses its final tile; this is not a shorter route or timing-only change.
        "superfast" => ReplayDifference.Other,
        _ => null
    };

    private static void AssertReplayExactBytes(string scenario, List<ReplayFrame> legacy, List<ReplayFrame> v2,
        List<ReplayDifference> differences)
    {
        if (scenario is not ("normal" or "fast")) return;
        Assert.Empty(differences);
        Assert.Equal(legacy.Select(FrameBytes), v2.Select(FrameBytes));
    }

    private static void AssertReplaySealedCorner(List<ReplayFrame> legacy, List<ReplayFrame> v2)
    {
        // The source is at the left edge, with the terminal door above it and blockers to its right/below.
        // Original legacy cuts between those blockers; v2 cannot transit through the door to detour.
        Assert.Equal("mv 1,2,0", legacy[0].Status);
        Assert.Equal((1, 2, 0d), legacy[1].Location);
        Assert.All(v2, frame => Assert.Equal((0, 1, 0d), frame.Location));
        Assert.All(v2, frame => Assert.DoesNotContain("mv ", frame.Status));
    }

    private static void AssertReplayScenarioDifferences(string scenario, List<ReplayDifference> differences)
    {
        var trajectory = ReplayTrajectoryClass(scenario);
        if (trajectory == null)
        {
            Assert.All(differences, difference => Assert.Equal(ReplayDifference.TimingPhaseChange, difference));
            return;
        }
        Assert.Contains(trajectory.Value, differences);
        Assert.All(differences, difference => Assert.True(difference == trajectory || difference == ReplayDifference.TimingPhaseChange));
        if (scenario is "superfast" or "locked pending")
            Assert.Equal(1, differences.Count(difference => difference == trajectory));
    }

    private static ReplayInput ReplayFurniture(uint id, int x, int y, double z, double height,
        InteractionType interaction, bool walkable = true)
    {
        var record = new NavItemRecord(id, 1, z, height, walkable, false, interaction, "", 0,
            0, false, Array.AsReadOnly(new[] { y * 4 + x }), x, y);
        return new(ReplayInputKind.Furniture, ItemRecord: record);
    }

    private static List<ReplayFrame> ReplayMovement(IReadOnlyList<ReplayInput> stream, PathfindingEngine engine, bool collision = true)
    {
        using var fixture = new PlacedFurniRoomTests();
        fixture.ReplayNavigation(engine, collision);
        RoomUser? actor = null;
        var frames = new List<ReplayFrame>();
        foreach (var input in stream)
        {
            if (input.Kind == ReplayInputKind.Admit) actor = fixture.ReplayAdmission(engine);
            else if (input.Kind == ReplayInputKind.Tick) frames.Add(fixture.ReplayTick(actor!));
            else fixture.ApplyReplayInput(input, actor);
        }
        ReportReplayFrames(engine, frames);
        return frames;
    }

    private void ReplayNavigation(PathfindingEngine engine, bool collision)
    {
        _roomSettings.Values["pathfinding.stacktool_legacy_collision"] = collision ? "1" : "0";
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, new() { Engine = engine, StacktoolLegacyCollision = collision }, TestLogging.Navigation, new TestGroupManager(id => _groupLookup(id)), _database, TestNavigationRewards.Instance);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);
    }

    private RoomUser ReplayAdmission(PathfindingEngine engine)
    {
        var actor = Viewer(0, 1); actor.InternalRoomId = actor.VirtualId; actor.UserId = 7;
        if (engine == PathfindingEngine.V2) _room.GetGameMap().Navigation!.Admit(actor);
        else _room.GetGameMap().AddUserToMap(actor, actor.Coordinate);
        _room.ProcessRoom();
        _client.Packets.Clear();
        return actor;
    }

    private void ApplyReplayInput(ReplayInput input, RoomUser? actor)
    {
        switch (input.Kind)
        {
            case ReplayInputKind.Furniture: ApplyReplayFurniture(input.ItemRecord!); break;
            case ReplayInputKind.Move: actor!.MoveTo(input.X, input.Y); break;
            case ReplayInputKind.Cancel: actor!.ClearMovement(true); break;
            case ReplayInputKind.Speed: actor!.FastWalking = input.Value == 2; actor.SuperFastWalking = input.Value == 3; break;
            case ReplayInputKind.Lock: _room.GetGameMap().SetFloorStatus(input.X, input.Y, 0); break;
            case ReplayInputKind.Height:
                Assert.True(_room.GetRoomItemHandler().SetFloorItem(_room.GetRoomItemHandler().GetItem(input.ItemId), input.X, input.Y, input.Z));
                break;
            default: throw new InvalidOperationException($"Unexpected replay event {input.Kind}");
        }
    }

    private void ApplyReplayFurniture(NavItemRecord record)
    {
        var item = Furni(record.ItemId, record.Interaction, Plus.HabboHotel.Items.Wired.WiredBoxType.None);
        item.UserId = 7; item.Definition.Height = record.Height; item.Definition.Walkable = record.Walkable;
        item.Definition.Stackable = record.Walkable; item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, record.X, record.Y,
            record.Rotation, true, false, false, height: record.Z));
    }

    private ReplayFrame ReplayTick(RoomUser actor)
    {
        ExecutorTick();
        var packets = _client.Packets.Where(p => p.Header == ServerPacketHeader.UserUpdateComposer)
            .Select(p => p.Body.ToArray()).ToArray();
        var statuses = packets.SelectMany(ReadReplayStatuses).ToArray();
        return new((actor.X, actor.Y, actor.Z), string.Join('/', actor.Statusses.Select(s => $"{s.Key} {s.Value}")),
            packets, statuses);
    }

    private static IEnumerable<ReplayStatus> ReadReplayStatuses(byte[] packet)
    {
        var body = new FlashIncomingPacket { Buffer = packet.ToArray() };
        var count = body.ReadInt();
        for (var index = 0; index < count; index++)
        {
            var id = body.ReadInt(); var x = body.ReadInt(); var y = body.ReadInt(); var z = body.ReadString();
            var head = body.ReadInt(); var rotation = body.ReadInt(); var status = body.ReadString();
            yield return new(id, x, y, z, head, rotation, status);
        }
    }

    private static List<ReplayDifference> ClassifyReplayDifferences(List<ReplayFrame> legacy, List<ReplayFrame> v2,
        ReplayDifference? trajectory = null)
    {
        Assert.Equal(legacy.Count, v2.Count);
        var result = new List<ReplayDifference>();
        for (var tick = 0; tick < legacy.Count; tick++)
        {
            if (FrameBytes(legacy[tick]) == FrameBytes(v2[tick])) continue;
            Assert.True(ReplayTelemetryMatches(legacy[tick])); Assert.True(ReplayTelemetryMatches(v2[tick]));
            if (ReplayTimingDifference(legacy[tick], v2[tick])) result.Add(ReplayDifference.TimingPhaseChange);
            else
            {
                Assert.NotNull(trajectory);
                result.Add(trajectory.Value);
            }
        }
        return result;
    }

    private static void ReportReplayFrames(PathfindingEngine engine, List<ReplayFrame> frames)
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_REPLAY_FRAMES");
        if (!string.IsNullOrEmpty(output))
            File.AppendAllLines(output, new[] { System.Text.Json.JsonSerializer.Serialize(new { engine, frames }, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }) });
    }

    private static string FrameBytes(ReplayFrame frame) => string.Join('|', frame.RawPackets.Select(Convert.ToHexString));
    private static string WithoutMove(string status) => string.Join('/', status.Split('/').Where(part => !part.StartsWith("mv ")).Order());
    private static string ReplayDifferenceName(ReplayDifference category) => category switch
    {
        ReplayDifference.CornerFix => "corner fix", ReplayDifference.ShorterRoute => "shorter route",
        ReplayDifference.BlockedTileFix => "blocked-tile fix", ReplayDifference.TimingPhaseChange => "timing-phase change",
        ReplayDifference.StacktoolExclusion => "stacktool-exclusion", ReplayDifference.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static void ReportReplay(string name, IReadOnlyList<ReplayInput> stream, List<ReplayDifference> differences)
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_REPLAY_REPORT");
        if (string.IsNullOrEmpty(output)) return;
        var classifications = Enum.GetValues<ReplayDifference>()
            .Select(category => $"{ReplayDifferenceName(category)}={differences.Count(value => value == category)}");
        var reason = name == "superfast" ? "; other=original superfast-only route loses its final tile (legacy x=2, v2 x=3)" : "";
        File.AppendAllLines(output, new[] { $"{name}: inputs={stream.Count}; {string.Join("; ", classifications)}{reason}" });
    }

    public enum ReplayDifference { CornerFix, ShorterRoute, BlockedTileFix, TimingPhaseChange, StacktoolExclusion, Other }
    private enum ReplayInputKind { Furniture, Admit, Move, Speed, Lock, Cancel, Height, Tick }
    private sealed record ReplayInput(ReplayInputKind Kind, int X = 0, int Y = 0, double Z = 0,
        int Value = 0, uint ItemId = 0, NavItemRecord? ItemRecord = null);
    private sealed record ReplayStatus(int VirtualId, int X, int Y, string Z, int Head, int Rotation, string Status);
    private sealed record ReplayFrame((int X, int Y, double Z) Location, string Status, byte[][] RawPackets, ReplayStatus[] Packets);
}
