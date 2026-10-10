using System.Drawing;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// §16.3 Wired variable layer: @state writes, aliases and retargets enter the same per-gate sequencer.
public partial class PlacedFurniRoomTests
{
    private WiredVariableModule GateVariables(List<(Item Item, WiredVariableFrame Frame, string State)> notices)
    {
        var builtins = new RoomWiredBuiltinVariables(_room, stateChanged: (item, frame) => notices.Add((item, frame, item.LegacyDataString)));

        return new WiredVariableModule(_room.Id, new GateDirectory(_room.Id), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)), builtins);
    }

    private static readonly WiredVariableReference StateReference = new(WiredVariableTarget.Furni, "internal:@state");

    private sealed class GateDirectory(uint roomId) : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint id) => id == roomId ? 7u : null;
        public WiredVariableDefinition? Find(uint itemId) => null;
    }

    [Fact]
    public void GateVariableStateWriteFromAnotherThreadDefersTheWholeTransactionToTheOwner()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]) { Depth = 3 };
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame, origin: 2)).Result);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Empty(notices);
        Assert.Empty(module.DrainChanges());

        using (RoomOwnerScope.Enter(_room)) {
            Gates.Drain();
        }

        Assert.Equal("0", gate.LegacyDataString);
        var notice = Assert.Single(notices);
        Assert.Same(gate, notice.Item1);
        Assert.Same(frame, notice.Item2);
        Assert.Equal("0", notice.Item3);
        var change = Assert.Single(module.DrainChanges());
        Assert.Equal((1, 0, 2), (change.Before!.Value, change.After!.Value, change.Origin));
    }

    [Fact]
    public void GateVariableStateWriteRefusedOnTheOwnerNeverNotifiesOrRecordsAChange()
    {
        var gate = ClosableGate(width: 2);
        ActorOn(NonAnchor(gate));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame, origin: 2)).Result);

        using (RoomOwnerScope.Enter(_room)) {
            Gates.Drain();
        }

        Assert.Equal("1", gate.LegacyDataString);
        Assert.Empty(notices);
        Assert.Empty(module.DrainChanges());
        Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateVariableStateWriteOnTheOwnerRefusalReturnsFalseAndSuccessNotifiesOnce()
    {
        var gate = ClosableGate(width: 2);
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        var (actor, navigation) = ActorOn(NonAnchor(gate));
        using var owner = RoomOwnerScope.Enter(_room);
        Assert.False(module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame));
        navigation.Executor.Claims.Remove(actor);
        Assert.True(module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame));
        Assert.Single(notices);
        Assert.Equal("0", gate.LegacyDataString);
    }

    [Fact]
    public void GateOffOwnerVariableOpeningIsImmediateAndReadsBack()
    {
        var gate = ClosableGate(state: "0");
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 1, frame)).Result);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, module.Read(StateReference, holder, frame)!.Value);
        Assert.Equal(0, Gates.PendingCount);
        Assert.Single(notices);
    }

    [Fact]
    public void GateAbsoluteVariableOpeningQueuesBehindACloseAndNotifiesOnDrain()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        ClickFromPacketThread(gate);
        Assert.Equal(1, Gates.PendingCount);
        Assert.Equal(1, module.Read(StateReference, holder, frame)!.Value);
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 1, frame)).Result);
        Assert.Equal(2, Gates.PendingCount);
        Assert.Empty(notices);
        ExecutorTick();
        Assert.Equal(0, Gates.PendingCount);
        Assert.Single(notices);
        var change = Assert.Single(module.DrainChanges());
        Assert.Equal((0, 1), (change.Before!.Value, change.After!.Value));
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, module.Read(StateReference, holder, frame)!.Value);
    }

    [Fact]
    public void GateAbsoluteVariableCloseBehindAQueuedCloseIsNoChangeOnDrain()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        ClickFromPacketThread(gate);
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame)).Result);
        Assert.Equal(2, Gates.PendingCount);
        Assert.Empty(notices);
        ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal(0, Gates.PendingCount);
        Assert.Empty(notices);
    }

    [Fact]
    public void GateSequencedOffOwnerVariableCloseRunsBeforeALaterAbsoluteOpening()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame)).Result);
        Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false)).Result);
        Assert.Equal("1", gate.LegacyDataString);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(0, Gates.PendingCount);
        Assert.Single(notices);
    }

    [Fact]
    public void GateSequencedPublicReadsUseCommittedStateWhileACloseIsQueuedAndLaterRefused()
    {
        var gate = ClosableGate(width: 2);
        ActorOn(NonAnchor(gate));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        ClickFromPacketThread(gate);
        Assert.Equal(1, Gates.PendingCount);
        Assert.Equal(1, module.Read(StateReference, holder, frame)!.Value);
        _room.RunFastPass(() => Assert.Equal(1, module.Read(StateReference, holder, frame)!.Value));
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, module.Read(StateReference, holder, frame)!.Value);
        Assert.Empty(notices);
        Assert.Empty(module.DrainChanges());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GateSequencedVariableTransformIsEvaluatedExactlyOnce(bool closing)
    {
        var gate = ClosableGate(state: closing ? "1" : "0");
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        var script = new Queue<int>(closing ? [0, 1] : [1, 0]);
        var calls = 0;
        Func<long, long> transform = _ => { calls++; return script.Dequeue(); };
        Assert.True(Task.Run(() => module.Change(StateReference, holder, WiredVariableMutation.Set, transform, frame, 2)).Result);
        Assert.Equal(1, calls);
        DrainOnOwner();
        Assert.Equal(1, calls);
        Assert.Equal(closing ? "0" : "1", gate.LegacyDataString);
        Assert.Single(notices);
    }

    [Fact]
    public void GateOperationConcurrentVariableIncrementsOnAMultiStateGateBothApply()
    {
        var gate = ClosableGate(state: "0");
        gate.Definition.Modes = 3;
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        using var evaluating = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        Func<long, long> slowIncrement = value => { evaluating.Set(); proceed.Wait(TimeSpan.FromSeconds(5)); return value + 1; };
        var first = Task.Run(() => module.Change(StateReference, holder, WiredVariableMutation.Set, slowIncrement, frame));

        try {
            Assert.True(evaluating.Wait(TimeSpan.FromSeconds(5)));
            var second = Task.Run(() => module.Change(StateReference, holder, WiredVariableMutation.Set, value => value + 1, frame));
            Assert.True(second.Wait(TimeSpan.FromSeconds(5)));
        }
        finally {
            proceed.Set();
        }

        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        DrainOnOwner();
        Assert.Equal("2", gate.LegacyDataString);
        Assert.Equal(0, Gates.PendingCount);
        Assert.Equal(2, notices.Count);
    }

    [Fact]
    public void GateOperationNestedWriteDuringAReplayAppendsBehindTheExistingFollower()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        GateTransition? nested = null;
        var builtins = new RoomWiredBuiltinVariables(_room, stateChanged: (item, _) =>
            nested = GateTransitionService.Apply(item, "2", GateCloseReason.Wired, persist: false));
        var module = new WiredVariableModule(_room.Id, new GateDirectory(_room.Id), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)), builtins);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame)).Result);
        Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false)).Result);
        DrainOnOwner();
        Assert.Equal(GateTransition.Queued, nested);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, Gates.PendingCount);
        DrainOnOwner();
        Assert.Equal("2", gate.LegacyDataString);
        Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateOperationPreparedVariableCloseKeepsItsPositionAheadOfAFollowerSubmittedDuringEvaluation()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        using var evaluating = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        Func<long, long> slowClose = _ => { evaluating.Set(); proceed.Wait(TimeSpan.FromSeconds(5)); return 0; };
        var first = Task.Run(() => module.Change(StateReference, holder, WiredVariableMutation.Set, slowClose, frame));

        try {
            Assert.True(evaluating.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(GateTransition.Queued, Task.Run(() => GateTransitionService.Apply(gate, "1", GateCloseReason.Wired, persist: false)).Result);
        }
        finally {
            proceed.Set();
        }

        Assert.True(first.Result);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(0, Gates.PendingCount);
        Assert.Single(notices);
    }

    private sealed class AliasDirectory(uint roomId) : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint id) => id == roomId ? 7u : null;
        public WiredVariableDefinition? Find(uint itemId) => itemId == 30
            ? new(30, roomId, 7, "echo", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true,
                Link: new(roomId, new(WiredVariableTarget.Furni, "internal:@state"), false)) : null;
    }

    private static readonly WiredVariableReference AliasReference = new(WiredVariableTarget.Furni, "custom:30");

    private WiredVariableModule AliasVariables(List<(Item Item, WiredVariableFrame Frame, string State)> notices)
    {
        var builtins = new RoomWiredBuiltinVariables(_room, stateChanged: (item, frame) => notices.Add((item, frame, item.LegacyDataString)));

        return new WiredVariableModule(_room.Id, new AliasDirectory(_room.Id), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)), builtins);
    }

    [Fact]
    public void GateLaneAliasOpeningBehindAQueuedCloseQueuesAndAppliesAtTheDrain()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = AliasVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        ClickFromPacketThread(gate);
        Assert.True(Task.Run(() => module.Mutate(AliasReference, holder, WiredVariableMutation.Set, 1, frame)).Result);
        Assert.Equal(2, Gates.PendingCount);
        Assert.Empty(notices);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Single(notices);
        Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateLaneAliasCloseFromAnotherThreadQueuesInsteadOfBeingRefused()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = AliasVariables(notices);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        Assert.True(Task.Run(() => module.Mutate(AliasReference, holder, WiredVariableMutation.Set, 0, frame)).Result);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, Gates.PendingCount);
        DrainOnOwner();
        Assert.Equal("0", gate.LegacyDataString);
        Assert.Single(notices);
    }

    private sealed class RetargetDirectory(uint roomId) : IWiredVariableDirectory
    {
        public bool ToState { get; set; }
        public uint? GetRoomOwner(uint id) => id == roomId ? 7u : null;
        public WiredVariableDefinition? Find(uint itemId) => itemId switch
        {
            30 => new(30, roomId, 7, "echo", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true,
                Link: new(roomId, new(WiredVariableTarget.Furni, ToState ? "internal:@state" : "custom:31"), false)),
            31 => new(31, roomId, 7, "points", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true),
            _ => null
        };
    }

    private static readonly WiredVariableReference PointsReference = new(WiredVariableTarget.Furni, "custom:31");

    private (WiredVariableModule Module, RetargetDirectory Directory, WiredVariableHolder Holder, WiredVariableFrame Frame,
        List<(Item Item, WiredVariableFrame Frame, string State)> Notices) RetargetWorld(Item gate, bool toState)
    {
        var notices = new List<(Item Item, WiredVariableFrame Frame, string State)>();
        var directory = new RetargetDirectory(_room.Id) { ToState = toState };
        var builtins = new RoomWiredBuiltinVariables(_room, stateChanged: (item, frame) => notices.Add((item, frame, item.LegacyDataString)));
        var module = new WiredVariableModule(_room.Id, directory, new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)), builtins);
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]);
        Assert.True(module.Change(PointsReference, holder, WiredVariableMutation.Give, _ => 5, frame));

        return (module, directory, holder, frame, notices);
    }

    [Fact]
    public void GateRetargetNonStateAliasBecomingStateBeforeAdmissionRetriesIntoTheLane()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var (module, directory, holder, frame, notices) = RetargetWorld(gate, toState: false);
        ClickFromPacketThread(gate);
        module.ResolutionHook = attempt =>
        {
            if (attempt == 1) {
                directory.ToState = true;
            }
        };
        var calls = 0;
        Assert.True(Task.Run(() => module.Change(AliasReference, holder, WiredVariableMutation.Set, _ => { calls++; return 1; }, frame)).Result);
        Assert.Equal(2, Gates.PendingCount);
        Assert.Equal(5, module.Read(PointsReference, holder, frame)!.Value);
        Assert.Equal(0, calls);
        DrainOnOwner();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Single(notices);
        Assert.Equal(5, module.Read(PointsReference, holder, frame)!.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GateRetargetStateAliasBecomingNonStateBeforeTheWriteReleasesTheLaneAndWritesTheNewTarget()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var (module, directory, holder, frame, notices) = RetargetWorld(gate, toState: true);
        module.ResolutionHook = attempt =>
        {
            if (attempt == 1) {
                directory.ToState = false;
            }
        };
        var calls = 0;
        Assert.True(Task.Run(() => module.Change(AliasReference, holder, WiredVariableMutation.Set, _ => { calls++; return 1; }, frame)).Result);
        Assert.Equal(1, module.Read(PointsReference, holder, frame)!.Value);
        Assert.Equal(1, calls);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Empty(notices);
        Assert.Equal(0, Gates.PendingCount);
        Assert.Equal(GateTransition.Applied, RunOwner(() => Gates.TryClose(gate, GateCloseReason.Click, "0", persist: false)));
    }

    [Fact]
    public void GateRetargetPersistentMismatchRefusesWithoutEffects()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var (module, directory, holder, frame, notices) = RetargetWorld(gate, toState: true);
        module.ResolutionHook = _ => directory.ToState = !directory.ToState;
        var calls = 0;
        Assert.False(Task.Run(() => module.Change(AliasReference, holder, WiredVariableMutation.Set, _ => { calls++; return 1; }, frame)).Result);
        Assert.Equal(0, calls);
        Assert.Equal(5, module.Read(PointsReference, holder, frame)!.Value);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Empty(notices);
        Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void GateRetargetQueuedReplayRefusesWhenItsAdmittedTargetWasRetargeted()
    {
        var gate = ClosableGate();
        ActorOn(new Point(0, 2));
        var (module, directory, holder, frame, notices) = RetargetWorld(gate, toState: true);
        var calls = 0;
        Assert.True(Task.Run(() => module.Change(AliasReference, holder, WiredVariableMutation.Set, _ => { calls++; return 0; }, frame)).Result);
        Assert.Equal(1, Gates.PendingCount);
        Assert.Equal(1, calls);
        directory.ToState = false;
        DrainOnOwner();
        Assert.Equal(1, calls);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(5, module.Read(PointsReference, holder, frame)!.Value);
        Assert.Empty(notices);
        Assert.Equal(0, Gates.PendingCount);
    }
}
