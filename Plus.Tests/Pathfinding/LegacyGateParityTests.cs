using System.Drawing;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// r17: under pathfinding.engine=legacy every gate writer runs its original code. The per-gate sequencer,
// occupancy guard and variable admission exist for the v2 engine only.
public partial class PlacedFurniRoomTests
{
    private Item LegacyGate(InteractionType kind = InteractionType.Gate, string state = "1")
    {
        var gate = ClosableGate(kind, width: 2, state: state);
        Assert.Null(_room.GetGameMap().Navigation);
        var occupant = Viewer(NonAnchor(gate).X, NonAnchor(gate).Y);
        _room.GetGameMap().AddUserToMap(occupant, NonAnchor(gate));
        return gate;
    }

    [Fact]
    public void LegacyGateClickClosesImmediatelyThroughANonAnchorOccupant()
    {
        var gate = LegacyGate();
        Task.Run(() => new InteractorGate().OnTrigger(_client, gate, 0, true)).Wait();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void LegacyGateWiredToggleWritesImmediatelyOffTheOwner()
    {
        var gate = LegacyGate();
        Task.Run(() => new InteractorGate().OnWiredTrigger(gate)).Wait();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void LegacyGenericSwitchTogglesAGateDirectly()
    {
        UseGameService("get_QuestManager", Proxy<IQuestManager>((_, _) => null));
        var gate = LegacyGate();
        Task.Run(() => new InteractorGenericSwitch(TestItemRuntime.Quests, TestItemRuntime.Rewards).OnTrigger(_client, gate, 0, true)).Wait();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void LegacyGuildGateAutoCloseChecksOnlyTheAnchorTile()
    {
        var gate = LegacyGate(InteractionType.GuildGate);
        gate.RequestUpdate(1, true); gate.ProcessUpdates();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, gate.UpdateCounter);
    }

    [Fact]
    public void LegacyVipGateClosesWithoutAnOccupancyCheck()
    {
        var gate = LegacyGate(InteractionType.GateVip);
        gate.RequestUpdate(1, true); gate.ProcessUpdates();
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, gate.UpdateCounter);
    }

    [Fact]
    public void LegacyMatchPositionBoxWritesAClosingStateDirectly()
    {
        var gate = LegacyGate();
        var box = new MatchPositionBox(_room, Furni(22, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition))
        { StringData = "1;0;0", ItemsData = $"{gate.Id}:1,1,0,0,0" };
        box.SetItems.TryAdd(gate.Id, gate);
        Assert.True(box.Execute());
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void LegacyModernToggleWritesAClosingStateDirectly()
    {
        var gate = LegacyGate();
        var action = ToggleAction(gate, out var context);
        Assert.True(action.Execute(context));
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, Gates.PendingCount);
    }

    [Fact]
    public void LegacyVariableStateWriteIsDirectAndNeverEntersTheSequencer()
    {
        var gate = LegacyGate(); var notices = new List<(Item, WiredVariableFrame, string)>();
        var module = GateVariables(notices); var holder = WiredVariableRuntimeFrames.FurniHolder(gate);
        var frame = new WiredVariableFrame(_room.Id, [holder]); var resolutions = 0;
        module.ResolutionHook = _ => resolutions++;
        Assert.True(Task.Run(() => module.Mutate(StateReference, holder, WiredVariableMutation.Set, 0, frame)).Result);
        Assert.Equal("0", gate.LegacyDataString); Assert.Single(notices);
        Assert.Equal(0, Gates.PendingCount); Assert.Equal(0, resolutions);
    }

    [Fact]
    public void LegacyMannequinPacketsRejectNonMannequinItems()
    {
        var gate = LegacyGate(); _client.GetHabbo().Gender = "M"; _client.GetHabbo().Look = "hd-180-1.ch-210-66"; _client.GetHabbo().Clothing = new();
        new SetMannequinFigureEvent(new RoomItemMetadataService(Proxy<IRoomItemMetadataStore>((_, _) => throw new InvalidOperationException("Wrong type must not persist")), null!)).Parse(_client, ClientPacket((int)gate.Id)).Wait();
        Assert.Equal("1", gate.LegacyDataString);
    }

    [Fact]
    public void LegacyFastWiredPassDoesNotEnterTheRoomOwnerScope()
    {
        var owned = true;
        _room.RunFastPass(() => owned = RoomOwnerScope.IsOwner(_room));
        Assert.False(owned);
    }
}
