using System.Reflection;
using Plus.Core.Settings;
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
        Assert.Equal((finalX, finalY, finalZ), legacy[^1].Location);
        var differences = ClassifyReplayDifferences(legacy, v2);
        Assert.All(differences, difference => Assert.Equal(ReplayDifference.TimingPhaseChange, difference));
        if (scenario is "normal" or "fast" or "superfast")
        {
            Assert.Empty(differences);
            Assert.Equal(legacy.Select(FrameBytes), v2.Select(FrameBytes));
        }
        ReportReplay(scenario, stream, differences);
    }

    [Theory]
    [InlineData(true, 0, 0d)]
    [InlineData(false, 1, 1d)]
    public void ExecutorReplayStacktoolCollisionSettingMatchesLegacyPackets(bool collision, int finalX, double finalZ)
    {
        using var settings = new ReplaySettingsScope(collision);
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
        if (scenario is "walk magic" or "magic height")
        {
            stream.Add(ReplayFurniture(10, 1, 1, 5, 1, InteractionType.None, false));
            stream.Add(ReplayFurniture(11, 1, 1, .75, 0, InteractionType.WalkMagicTile));
        }
        stream.Add(new(ReplayInputKind.Admit));
        if (scenario is "fast" or "superfast") stream.Add(new(ReplayInputKind.Speed, Value: scenario == "fast" ? 2 : 3));
        stream.Add(new(ReplayInputKind.Move, scenario == "already there" ? 0 : scenario.Contains("magic") || scenario == "locked pending" ? 1 : 3, 1));
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
        return frames;
    }

    private void ReplayNavigation(PathfindingEngine engine, bool collision)
    {
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, new() { Engine = engine, StacktoolLegacyCollision = collision });
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

    private static List<ReplayDifference> ClassifyReplayDifferences(List<ReplayFrame> legacy, List<ReplayFrame> v2)
    {
        Assert.Equal(legacy.Count, v2.Count);
        var result = new List<ReplayDifference>();
        for (var tick = 0; tick < legacy.Count; tick++)
        {
            if (FrameBytes(legacy[tick]) == FrameBytes(v2[tick])) continue;
            Assert.Equal(legacy[tick].Location, v2[tick].Location);
            Assert.Equal(WithoutMove(legacy[tick].Status), WithoutMove(v2[tick].Status));
            var left = legacy[tick].Packets; var right = v2[tick].Packets;
            foreach (var status in left.Concat(right)) Assert.Equal(1, status.VirtualId);
            result.Add(ReplayDifference.TimingPhaseChange);
        }
        return result;
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
        File.AppendAllLines(output, new[] { $"{name}: inputs={stream.Count}; {string.Join("; ", classifications)}" });
    }

    private sealed class ReplaySettingsScope : IDisposable
    {
        private readonly FieldInfo _field = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previous;
        public ReplaySettingsScope(bool collision)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, new ReplaySettings(collision));
        }
        public void Dispose() => _field.SetValue(null, _previous);
    }

    private sealed class ReplaySettings(bool collision) : ISettingsManager
    {
        public string TryGetValue(string key) => TryGetValue(key, "0");
        public string TryGetValue(string key, string defaultValue) => GetOptionalValue(key) ?? defaultValue;
        public string? GetOptionalValue(string key) => key == "pathfinding.stacktool_legacy_collision" ? collision ? "1" : "0" : null;
        public Task Reload() => Task.CompletedTask;
    }

    public enum ReplayDifference { CornerFix, ShorterRoute, BlockedTileFix, TimingPhaseChange, StacktoolExclusion, Other }
    private enum ReplayInputKind { Furniture, Admit, Move, Speed, Lock, Cancel, Height, Tick }
    private sealed record ReplayInput(ReplayInputKind Kind, int X = 0, int Y = 0, double Z = 0,
        int Value = 0, uint ItemId = 0, NavItemRecord? ItemRecord = null);
    private sealed record ReplayStatus(int VirtualId, int X, int Y, string Z, int Head, int Rotation, string Status);
    private sealed record ReplayFrame((int X, int Y, double Z) Location, string Status, byte[][] RawPackets, ReplayStatus[] Packets);
}
