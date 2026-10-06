using System.Collections.Concurrent;
using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void ExecutorReplayRecordsRealPublishedInputsAndRemapsCapturedRoomCommandLifetimes()
    {
        var capture = CaptureMovementStream();
        var moves = capture.Inputs.Where(input => input.Command != null).Select(input => input.Command!).ToArray();
        Assert.Equal(new long[] { 1, 2 }, moves.Select(command => command.Sequence));
        Assert.All(moves, command => Assert.Equal(MoveOrigin.User, command.Origin));
        var roomCommands = capture.Inputs.Where(input => input.RoomCommand != null).Select(input => input.RoomCommand!).ToArray();
        Assert.Equal(new[] { RoomCommandKind.Admit, RoomCommandKind.Cancel }, roomCommands.Select(command => command.Kind));
        Assert.All(roomCommands, command => Assert.Equal(capture.LifetimeId, command.LifetimeId));
        var items = capture.Inputs.Where(input => input.ItemRecord != null).Select(input => input.ItemRecord!).ToArray();
        Assert.Equal(new[] { .75, 1.25 }, items.Select(item => item.Z));
        Assert.True(items[1].Version > items[0].Version);
        var v2 = ReplayCapturedMovement(capture, PathfindingEngine.V2);
        var legacy = ReplayCapturedMovement(capture, PathfindingEngine.Legacy);
        var shadow = ReplayCapturedMovement(capture, PathfindingEngine.Shadow);
        Assert.NotEqual(capture.LifetimeId, v2.LifetimeId);
        Assert.Equal(capture.Frames.Select(FrameBytes), v2.Frames.Select(FrameBytes));
        Assert.Equal(legacy.Frames.Select(FrameBytes), shadow.Frames.Select(FrameBytes));
        Assert.Equal((1, 1, 1.25), v2.Frames[^1].Location);
        Assert.All(ClassifyReplayDifferences(legacy.Frames, v2.Frames),
            category => Assert.Equal(ReplayDifference.TimingPhaseChange, category));
    }

    private static CapturedMovementStream CaptureMovementStream()
    {
        using var fixture = new PlacedFurniRoomTests();
        fixture.ReplayNavigation(PathfindingEngine.V2, true);
        var tile = fixture.Add(10, 1, 1, z: .75, type: InteractionType.WalkMagicTile);
        var navigation = fixture._room.GetGameMap().Navigation!;
        var inputs = new List<CapturedMovementInput> { new(ItemRecord: navigation.Inputs.Read(tile.Id)!) };
        var frames = new List<ReplayFrame>();
        var actor = fixture.Viewer(0, 1);
        actor.InternalRoomId = actor.VirtualId;
        actor.UserId = 7;
        navigation.Admit(actor);
        inputs.Add(new(RoomCommand: CaptureQueuedCommand(navigation)));
        fixture.CaptureReplayTick(actor, inputs, frames);
        CapturePublishedMove(actor, 3, 1, inputs);
        fixture.CaptureReplayTick(actor, inputs, frames);
        actor.ClearMovement(true);
        inputs.Add(new(RoomCommand: CaptureQueuedCommand(navigation)));
        fixture.CaptureReplayTick(actor, inputs, frames);
        CapturePublishedMove(actor, 1, 1, inputs);
        fixture.CaptureReplayTick(actor, inputs, frames);
        fixture.CaptureReplayTick(actor, inputs, frames);
        Assert.True(fixture._room.GetRoomItemHandler().SetFloorItem(tile, 1, 1, 1.25));
        inputs.Add(new(ItemRecord: navigation.Inputs.Read(tile.Id)!));
        fixture.CaptureReplayTick(actor, inputs, frames);
        fixture.CaptureReplayTick(actor, inputs, frames);

        return new(inputs.AsReadOnly(), frames, actor.Movement.LifetimeId);
    }

    private static void CapturePublishedMove(RoomUser actor, int x, int y, List<CapturedMovementInput> inputs)
    {
        actor.MoveTo(x, y);
        inputs.Add(new(Command: Assert.IsType<MoveCommand>(actor.Movement.Commands.Read())));
    }

    private void CaptureReplayTick(RoomUser actor, List<CapturedMovementInput> inputs, List<ReplayFrame> frames)
    {
        inputs.Add(new(Tick: true));
        frames.Add(ReplayTick(actor));
    }

    private static RoomCommand CaptureQueuedCommand(RoomNavigation navigation)
    {
        var queue = (RoomCommandQueue)typeof(RoomNavigation)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(navigation)!;
        var commands = (ConcurrentQueue<RoomCommand>)typeof(RoomCommandQueue)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;

        return Assert.Single(commands.ToArray());
    }

    private static CapturedMovementReplay ReplayCapturedMovement(CapturedMovementStream capture, PathfindingEngine engine)
    {
        using var fixture = new PlacedFurniRoomTests();
        fixture.ReplayNavigation(engine, true);
        var actor = fixture.Viewer(0, 1);
        actor.InternalRoomId = actor.VirtualId;
        actor.UserId = 7;
        var frames = new List<ReplayFrame>();

        foreach (var input in capture.Inputs)
        {
            if (input.Tick)
            {
                frames.Add(fixture.ReplayTick(actor));
            }
            else if (input.ItemRecord != null)
            {
                fixture.ApplyCapturedItem(input.ItemRecord);
            }
            else if (input.Command != null)
            {
                ReplayCapturedMove(actor, input.Command, engine);
            }
            else
            {
                fixture.ReplayCapturedRoomCommand(actor, input.RoomCommand!, engine);
            }
        }

        return new(frames, actor.Movement.LifetimeId);
    }

    private void ApplyCapturedItem(NavItemRecord record)
    {
        var existing = _room.GetRoomItemHandler().GetItem(record.ItemId);

        if (existing == null)
        {
            ApplyReplayFurniture(record);

            return;
        }

        Assert.True(_room.GetRoomItemHandler().SetFloorItem(existing, record.X, record.Y, record.Z));
    }

    private static void ReplayCapturedMove(RoomUser actor, MoveCommand command, PathfindingEngine engine)
    {
        if (engine == PathfindingEngine.V2)
        {
            Assert.True(actor.Movement.Commands.Publish(command));
        }
        else
        {
            actor.MoveTo(command.X, command.Y);
        }
    }

    private void ReplayCapturedRoomCommand(RoomUser actor, RoomCommand command, PathfindingEngine engine)
    {
        if (engine == PathfindingEngine.V2)
        {
            var navigation = _room.GetGameMap().Navigation!;
            var queue = (RoomCommandQueue)typeof(RoomNavigation)
                .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(navigation)!;
            queue.Enqueue(command with
            {
                Actor = actor,
                LifetimeId = actor.Movement.LifetimeId
            });

            return;
        }

        if (command.Kind == RoomCommandKind.Cancel)
        {
            actor.ClearMovement(true);
        }
        else
        {
            Assert.Equal(RoomCommandKind.Admit, command.Kind);
            _room.GetGameMap().AddUserToMap(actor, actor.Coordinate);
            actor.UpdateNeeded = true;
        }
    }

    private sealed record CapturedMovementInput(MoveCommand? Command = null, NavItemRecord? ItemRecord = null,
        RoomCommand? RoomCommand = null, bool Tick = false);
    private sealed record CapturedMovementStream(IReadOnlyList<CapturedMovementInput> Inputs, List<ReplayFrame> Frames, long LifetimeId);
    private sealed record CapturedMovementReplay(List<ReplayFrame> Frames, long LifetimeId);
}
