using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

// Writes that land later or outside a use (dice results, vending, item cycles, queued gates) raise
// wf_trg_state_changed once per actual write; whoever reports a write takes it, so nothing is reported twice.
public partial class PlacedFurniRoomTests
{
    [Fact]
    public void DiceRollStartResultAndOffAreEachReportedOnce()
    {
        var dice = PlacedFurni(40, InteractionType.Dice, 7);
        WatchState(dice);
        WhisperOnChange(104, dice);
        Viewer(2, 3);

        UseService().RollDice(_room, _client, new(dice.Id, 0));
        Cycle();
        Assert.Equal("-1", dice.LegacyDataString);
        Assert.Equal((0, 1), StateLines());
        Assert.Equal(1, Whispers()); // the roll is the user's

        for (var tick = 0; tick < 5 && dice.LegacyDataString == "-1"; tick++)
        {
            dice.ProcessUpdates();
        }

        Cycle();
        Assert.NotEqual("-1", dice.LegacyDataString);
        Assert.Equal((0, 2), StateLines());
        Assert.Equal(1, Whispers()); // the landing is the room's

        UseService().TurnOffDice(_room, _client, dice.Id);
        Cycle();
        Assert.Equal("0", dice.LegacyDataString);
        Assert.Equal((0, 3), StateLines());
    }

    [Fact]
    public void VendingDispenseAndResetAreEachReportedOnce()
    {
        var vending = PlacedFurni(41, InteractionType.VendingMachine, 2);
        vending.Definition.VendingIds.Add(5);
        WatchState(vending);
        Viewer(2, 3);

        UseItem(vending);
        Assert.Equal("1", vending.LegacyDataString);
        Assert.Equal((1, 1), StateLines());

        for (var tick = 0; tick < 5 && vending.LegacyDataString == "1"; tick++)
        {
            vending.ProcessUpdates();
        }

        Cycle();
        Assert.Equal("0", vending.LegacyDataString);
        Assert.Equal((1, 2), StateLines());
    }

    [Fact]
    public void RepeatedWritesAreReportedOncePerActualChange()
    {
        var lamp = Toggle(30);
        WatchState(lamp);

        lamp.LegacyDataString = "1";
        lamp.LegacyDataString = "1";
        lamp.LegacyDataString = "0";
        lamp.LegacyDataString = "1";
        lamp.UpdateState();
        Cycle();

        Assert.Equal((0, 3), StateLines());
        Cycle();
        Assert.Equal((0, 3), StateLines());
    }

    [Fact]
    public void AnEarlierCycleWriteIsNotTakenByALaterUnchangedWrite()
    {
        var lamp = Toggle(30);
        WatchState(lamp);

        lamp.LegacyDataString = "1"; // a cycle write, no publisher of its own
        var mark = FurnitureStateEvents.Mark();
        lamp.LegacyDataString = "1"; // a publisher writing the same state: no write of its own
        Assert.False(FurnitureStateEvents.TakeWriteSince(lamp, mark));
        Cycle();

        Assert.Equal((0, 1), StateLines());
    }

    [Fact]
    public void AnEffectWriteAndAnEarlierCycleWriteAreEachReportedOnce()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        var button = ToggleAt(31, 1, 3);
        TogglesOnUse(120, button, lamp);
        Viewer(0, 3);

        lamp.LegacyDataString = "1"; // a cycle write, queued
        UseItem(button);              // the use stack toggles the lamp back and reports its own write

        Assert.Equal("0", lamp.LegacyDataString);
        Assert.Equal((0, 2), StateLines());
    }

    [Fact]
    public void QueuedModernGateEffectStillReportsItsWriteAfterTheUserLeft()
    {
        _room.EnableV2Movement();
        var gate = ClosableGate();
        WatchState(gate);
        var button = ToggleAt(31, 3, 3);
        TogglesOnUse(120, button, gate);
        ExecutorActor(0, 2);

        UseItem(button);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, Gates.PendingCount);
        _room.GetRoomUserManager().RemoveUserFromRoom(_client, false, false);
        ExecutorTick();
        ExecutorTick();

        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal((0, 1), StateLines());
    }

    [Fact]
    public void QueuedGateClickNamesItsUserOnceWhenItLands()
    {
        _room.EnableV2Movement();
        var gate = ClosableGate();
        WatchState(gate);
        WhisperOnChange(104, gate);
        ExecutorActor(0, 2);

        UseItem(gate);
        var whispers = 0;

        for (var tick = 0; tick < 3; tick++)
        {
            ExecutorTick();
            whispers += Whispers();
        }

        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal((1, 1), StateLines());
        Assert.Equal(1, whispers);
    }

    [Fact]
    public void SynchronousGateInsideAUseCannotTakeAnOlderQueuedWrite()
    {
        _room.EnableV2Movement();
        var gate = ClosableGate(state: "0");
        WatchState(gate);
        WhisperOnChange(104, gate);
        Viewer(0, 0);

        gate.LegacyDataString = "1"; // older writes on this thread, queued for the room's pass
        gate.LegacyDataString = "0";
        _client.Sent.Clear();
        UseItem(gate);               // opens at once, inside the use's capture

        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal((1, 3), StateLines());
        Assert.Equal(1, Whispers()); // only the use's own write names the user
    }

    [Fact]
    public void ACapturedWriteKeepsItsUserWhileAnotherThreadRunsTheRoomPass()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        WhisperOnChange(104, lamp);
        var user = Viewer(0, 0);

        var capture = FurnitureStateEvents.Capture(_room);

        try
        {
            lamp.LegacyDataString = "1";
            Task.Run(() => _room.GetWired().OnCycle()).GetAwaiter().GetResult();
        }
        finally { capture.Dispose(); }

        FurnitureStateEvents.Publish(_room, user, capture.Transitions);
        Cycle();

        Assert.Equal((0, 1), StateLines());
        Assert.Equal(1, Whispers());
    }

    [Fact]
    public void AnInteractorThatWritesAndThrowsStillReportsTheWriteOnce()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        WhisperOnChange(104, lamp);
        Viewer(0, 0);
        typeof(Item).GetField("_interactors", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lamp, new ThrowingInteractors());

        var error = Assert.Throws<InvalidOperationException>(() => UseService().Use(_room, _client, new(lamp.Id, 0)));
        Assert.Equal("interactor failed", error.Message);
        Cycle();
        Assert.Equal((0, 1), StateLines());
        Assert.Equal(1, Whispers());

        lamp.LegacyDataString = "0"; // the capture is gone: this write is the room's
        Cycle();
        Assert.Equal((0, 2), StateLines());
        Assert.Equal(1, Whispers());
    }

    [Fact]
    public void NestedCapturesRestoreTheOuterScopeAndKeepToTheirRoom()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        var otherRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));

        using (var outer = FurnitureStateEvents.Capture(_room))
        {
            using (var inner = FurnitureStateEvents.Capture(_room))
            {
                lamp.LegacyDataString = "1";
                Assert.Single(inner.Transitions);
            }

            using (FurnitureStateEvents.Capture(otherRoom))
            {
                lamp.LegacyDataString = "0";
            }

            Assert.Single(outer.Transitions);
            Assert.Throws<InvalidOperationException>(new Action(() =>
            {
                using (FurnitureStateEvents.Capture(_room))
                {
                    lamp.LegacyDataString = "1";
                    throw new InvalidOperationException();
                }
            }));
            lamp.LegacyDataString = "0";
            Assert.Equal(2, outer.Transitions.Count);
        }

        lamp.LegacyDataString = "1";
        Cycle();

        Assert.Equal((0, 1), StateLines()); // only the write made after every capture closed is queued
    }

    [Fact]
    public void ANewItemsPlacementStateIsNotAChangeButAMovedItemsIs()
    {
        var placed = PlacedFurni(40, InteractionType.Dice, 7);
        WatchState(placed);
        placed.LegacyDataString = "-1";
        Cycle();
        Assert.Equal((0, 1), StateLines());

        var admitted = Furni(42, InteractionType.Dice, WiredBoxType.None);
        admitted.Definition.Modes = 7;
        InitializeNativeState(admitted);
        admitted.LegacyDataString = "-1";
        Stack(130, 1, "wf_trg_state_changed", ChangeLine, admitted);
        Viewer(0, 3);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, admitted, 2, 2, 0, true, false, false));
        Assert.Equal("0", admitted.LegacyDataString);
        Cycle();
        Assert.Equal((0, 1), StateLines()); // the admitted dice's reset is its initial state

        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, placed, 3, 2, 0, false, false, false));
        Assert.Equal("0", placed.LegacyDataString);
        Cycle();
        Assert.Equal((0, 2), StateLines()); // the moved dice's reset is a change
    }

    [Fact]
    public void WritesFromAnEarlierPlacementAreNeverReported()
    {
        var lamp = Toggle(30);
        WatchState(lamp);

        lamp.LegacyDataString = "1";
        _room.GetRoomItemHandler().RemoveFurniture(_client, lamp.Id);
        lamp.LegacyDataString = "0"; // detached
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, lamp, 3, 3, 0, true, false, false));
        Cycle();
        Assert.Equal((0, 0), StateLines()); // the same instance, placed again, does not replay its old writes

        lamp.LegacyDataString = "1";
        Cycle();
        Assert.Equal((0, 1), StateLines());
    }

    [Fact]
    public void AFullQueueDropsTheOverflowWithANoteAndCleanupEmptiesIt()
    {
        var lamp = Toggle(30);

        for (var write = 0; write < WiredComponent.MaxQueuedStateWrites + 4; write++)
        {
            lamp.LegacyDataString = write % 2 == 0 ? "1" : "0";
        }

        Cycle();
        Assert.Contains(_room.GetWired().ReadLogs(0, 100).Entries,
            entry => entry.Message.Contains("4 furni state changes were dropped", StringComparison.Ordinal));
        Assert.Equal(0, QueuedStateWrites());

        Cycle();
        Assert.Single(_room.GetWired().ReadLogs(0, 100, -1, "were dropped").Entries);

        lamp.LegacyDataString = "1";
        lamp.LegacyDataString = "0";
        Assert.Equal(2, QueuedStateWrites());
        _room.GetWired().Cleanup();
        Assert.Equal(0, QueuedStateWrites());
    }

    [Fact]
    public void QueuedBuiltinGateStateWriteIsReportedOnceAfterItsUserLeft()
    {
        _room.EnableV2Movement();
        var gate = ClosableGate();
        WatchState(gate);
        WhisperOnChange(104, gate);
        var actor = ExecutorActor(0, 2);
        var module = new WiredVariableModule(_room.Id, new GateDirectory(_room.Id), new MemoryWiredVariableStore(),
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)),
            new RoomWiredBuiltinVariables(_room, stateChanged: _room.GetWired().PublishBuiltinStateChanged));
        var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Use)
        {
            Actor = actor
        },
            new(() => _room.GetRoomItemHandler().GetFloor, () => _room.GetRoomUserManager().GetUserList()), _room.GetWired());
        var frame = new WiredVariableFrame(_room.Id, [holder]) { RuntimeContext = context, Depth = 1 };

        // A close from another thread waits for the room's owner; the user leaves before it lands.
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame, origin: 2)).GetAwaiter().GetResult());
        Assert.Equal("1", gate.LegacyDataString);
        _room.GetRoomUserManager().RemoveUserFromRoom(_client, false, false);
        _client.Sent.Clear();

        using (RoomOwnerScope.Enter(_room))
        {
            Gates.Drain();
        }

        Cycle();

        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal((0, 1), StateLines());
        Assert.Equal(0, Whispers());
        Cycle();
        Assert.Equal((0, 1), StateLines());
    }

    private Item PlacedFurni(uint id, InteractionType kind, int modes)
    {
        var item = Furni(id, kind, WiredBoxType.None);
        item.Definition.Modes = modes;
        InitializeNativeState(item);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));

        return item;
    }

    private Item ToggleAt(uint id, int x, int y)
    {
        var item = Toggle(id, place: false);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, x, y, 0, true, false, false));

        return item;
    }

    // A stack on its own tile: using the button toggles the target.
    private void TogglesOnUse(uint id, Item button, Item target)
    {
        var trigger = WiredBox(id, "wf_trg_stuff_state", 0, 1);
        Assert.True(trigger.TryValidateConfiguration(new()
        {
            IntParams = [0, 100],
            SelectedItems = [button.Id]
        }, out var config, out var error), error);
        trigger.ApplyConfiguration(config);
        Assert.True(_room.GetWired().AddBox(trigger));
        var toggle = WiredBox(id + 1, "wf_act_toggle_state", 0, 1);
        Assert.True(toggle.TryValidateConfiguration(new()
        {
            IntParams = [0, 100],
            SelectedItems = [target.Id]
        }, out config, out error), error);
        toggle.ApplyConfiguration(config);
        Assert.True(_room.GetWired().AddBox(toggle));
    }

    private static FurnitureUseService UseService() => new(null!, TestItemRuntime.Quests);

    private void Cycle()
    {
        _room.GetWired().OnFastCycle();
        _room.GetWired().OnCycle();
    }

    private int Whispers() => _client.Sent.Count(header => header == ServerPacketHeader.WhisperComposer);

    private int QueuedStateWrites() =>
        (int)typeof(WiredComponent).GetField("_queuedStateWrites", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetWired())!;

    private sealed class ThrowingInteractors : IItemInteractorFactory
    {
        public IFurniInteractor Create(Item item, TimeProvider timeProvider) => new Throwing();

        private sealed class Throwing : IFurniInteractor
        {
            public void OnPlace(GameClient? session, Item item)
            {
            }
            public void OnRemove(GameClient? session, Item item)
            {
            }
            public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
            {
                item.LegacyDataString = "1";
                throw new InvalidOperationException("interactor failed");
            }
            public void OnWiredTrigger(Item item)
            {
            }
        }
    }
}
