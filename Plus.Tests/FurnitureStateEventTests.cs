using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

// A user's use raises wf_trg_stuff_state once per request and wf_trg_state_changed once per state it
// actually wrote, including gate writes the v2 sequencer applies after the request returned.
public partial class PlacedFurniRoomTests
{
    private const string UseLine = "used", ChangeLine = "changed";

    [Fact]
    public void UsingAToggleRaisesOneUseAndOneStateChange()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        Viewer(0, 0);

        UseItem(lamp);

        Assert.Equal("1", lamp.LegacyDataString);
        Assert.Equal((1, 1), StateLines());
    }

    [Fact]
    public void UseWithoutRightsRaisesTheUseButNoStateChange()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        Viewer(0, 0);
        _room.OwnerName = "someone else"; _room.UsersWithRights = [];

        UseItem(lamp);

        Assert.Equal("0", lamp.LegacyDataString);
        Assert.Equal((1, 0), StateLines());
    }

    [Fact]
    public void LegacyGateClickRaisesTheUseOnce()
    {
        var gate = ClosableGate(state: "0");
        WatchState(gate);
        Viewer(0, 0);

        UseItem(gate);

        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal((1, 1), StateLines());
    }

    [Theory]
    [InlineData(InteractionType.Gate)]
    [InlineData(InteractionType.GuildGate)]
    [InlineData(InteractionType.GateVip)]
    public void SequencedGateOpenReportsItsStateChangeOnce(InteractionType kind)
    {
        _room.EnableV2Movement();
        var gate = ClosableGate(kind, state: "0");
        WatchState(gate);
        ExecutorActor(0, 2);

        UseItem(gate);

        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal((1, 1), StateLines());
    }

    [Theory]
    [InlineData(InteractionType.Gate)]
    [InlineData(InteractionType.GuildGate)]
    [InlineData(InteractionType.GateVip)]
    public void SequencedGateCloseReportsItsStateChangeOnlyWhenTheQueuedWriteLands(InteractionType kind)
    {
        _room.EnableV2Movement();
        var gate = ClosableGate(kind);
        WatchState(gate);
        ExecutorActor(0, 2);

        UseItem(gate);
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(1, Gates.PendingCount);
        Assert.Equal((1, 0), StateLines());

        ExecutorTick(); ExecutorTick();
        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal((1, 1), StateLines());
    }

    [Fact]
    public void SequencedGateCloseStillReportsItsChangeAfterTheUserLeft()
    {
        _room.EnableV2Movement();
        var gate = ClosableGate();
        WatchState(gate);
        var actor = ExecutorActor(0, 2);

        UseItem(gate);
        _room.GetRoomUserManager().RemoveUserFromRoom(_client, false, false);
        Assert.Null(_room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId));
        ExecutorTick(); ExecutorTick();

        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal((1, 1), StateLines());
    }

    [Fact]
    public void StateChangeIsReportedOnlyForTheFurniInstanceTheRoomHolds()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        var replacement = Toggle(30, place: false);
        _room.GetRoomItemHandler().RemoveFurniture(_client, lamp.Id);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, replacement, 3, 3, 0, true, false, false));

        FurnitureStateEvents.Publish(_room, null, lamp);
        _room.GetWired().OnFastCycle(); _room.GetWired().OnCycle();
        Assert.Equal((0, 0), StateLines());

        FurnitureStateEvents.Publish(_room, null, replacement);
        _room.GetWired().OnFastCycle(); _room.GetWired().OnCycle();
        Assert.Equal((0, 1), StateLines());
    }

    [Fact]
    public void StateChangeNamesOnlyAUserWhoseVisitIsStillThisRoom()
    {
        var lamp = Toggle(30);
        WatchState(lamp);
        WhisperOnChange(104, lamp);
        var user = Viewer(0, 0);

        FurnitureStateEvents.Publish(_room, user, lamp);
        _room.GetWired().OnFastCycle(); _room.GetWired().OnCycle();
        Assert.Equal((0, 1), StateLines());
        Assert.Contains(ServerPacketHeader.WhisperComposer, _client.Sent);

        _client.Sent.Clear();
        _client.GetHabbo().CurrentRoom = null; // moved on while the room still lists the avatar
        FurnitureStateEvents.Publish(_room, user, lamp);
        _room.GetWired().OnFastCycle(); _room.GetWired().OnCycle();
        Assert.Equal((0, 2), StateLines());
        Assert.DoesNotContain(ServerPacketHeader.WhisperComposer, _client.Sent);
    }

    // A third stack that whispers to the user the state change names, if it names one.
    private void WhisperOnChange(uint id, Item watched)
    {
        var trigger = WiredBox(id, "wf_trg_state_changed", 1, 0);
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [0, 100], SelectedItems = [watched.Id] }, out var config, out var error), error);
        trigger.ApplyConfiguration(config); Assert.True(_room.GetWired().AddBox(trigger));
        var whisper = WiredBox(id + 1, "wf_act_show_message", 1, 0);
        Assert.True(whisper.TryValidateConfiguration(new() { IntParams = [0, 0, 34, -1], Text = "changed" }, out config, out error), error);
        whisper.ApplyConfiguration(config); Assert.True(_room.GetWired().AddBox(whisper));
    }

    private Item Toggle(uint id, bool place = true)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None);
        item.Definition.Modes = 2;
        InitializeNativeState(item);
        if (place) Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));
        return item;
    }

    // Two real stacks on their own tiles: each trigger watches the furni and writes its own log line.
    private void WatchState(Item watched)
    {
        Stack(100, 2, "wf_trg_stuff_state", UseLine, watched);
        Stack(102, 3, "wf_trg_state_changed", ChangeLine, watched);
    }

    private void Stack(uint id, int x, string trigger, string line, Item watched)
    {
        var triggerBox = WiredBox(id, trigger, x, 0);
        Assert.True(triggerBox.TryValidateConfiguration(new() { IntParams = [0, 100], SelectedItems = [watched.Id] }, out var config, out var error), error);
        triggerBox.ApplyConfiguration(config); Assert.True(_room.GetWired().AddBox(triggerBox));
        var log = WiredBox(id + 1, "wf_act_log", x, 0);
        Assert.True(log.TryValidateConfiguration(new() { IntParams = [1, 0], Text = line }, out config, out error), error);
        log.ApplyConfiguration(config); Assert.True(_room.GetWired().AddBox(log));
    }

    private IWiredConfiguredItem WiredBox(uint id, string name, int x, int y)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None);
        item.Definition.InteractionName = item.Definition.ItemName = name;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, x, y, 0, true, false, false));
        return _room.GetWired().CreateConfiguredBox(item)!;
    }

    private void UseItem(Item item)
    {
        new FurnitureUseService(null!, TestItemRuntime.Quests).Use(_room, _client, new(item.Id, 0));
        _room.GetWired().OnFastCycle(); _room.GetWired().OnCycle();
    }

    private (int Used, int Changed) StateLines()
    {
        var lines = _room.GetWired().ReadLogs(0, 100).Entries.Select(entry => entry.Message).ToArray();
        return (lines.Count(line => line == UseLine), lines.Count(line => line == ChangeLine));
    }
}
