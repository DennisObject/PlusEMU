using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void CommitServiceReplacementCancelsAPendingSeatLandingBeforeAnyHooks()
    {
        Add(10, 1, 1, z: .25, seat: true);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick();
        actor.MoveTo(0, 2); ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Empty(events); Assert.False(actor.HasStatus("sit"));
        Assert.Contains("/mv 0,2,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((0, 2), (actor.X, actor.Y));
    }

    [Fact]
    public void BlockedStepPolicyCancelsStarvedIntentAfterOnlyEligibleTicks()
    {
        var actor = ExecutorConfiguredActor(new() { Engine = PathfindingEngine.V2,
            MaxExpansionsPerRoomTick = 0, MaxWalkStallTicks = 3 });
        actor.MoveTo(3, 1); ExecutorTick();
        Assert.True(actor.Movement.HasIntent); Assert.False(actor.HasStatus("mv"));
        actor.Freezed = true;
        for (var cycle = 0; cycle < 20; cycle++) ExecutorTick();
        Assert.True(actor.Movement.HasIntent); Assert.Equal(0, actor.Movement.StallTicks);
        actor.Freezed = false; ExecutorTick(); ExecutorTick();
        Assert.True(actor.Movement.HasIntent);
        ExecutorTick();
        Assert.False(actor.Movement.HasIntent); Assert.False(actor.IsWalking);
        Assert.Equal(0, actor.Movement.Route.Count);
    }

    [Fact]
    public void AnnounceServiceContestedDestinationGrantsOnlyTheFirstVirtualIdAClaim()
    {
        var first = ExecutorActor(0, 1);
        var second = ExecutorAdditionalBot(2, 1, 2);
        ExecutorTick();
        first.MoveTo(1, 1); second.MoveTo(1, 1); ExecutorTick();
        Assert.True(first.HasStatus("mv")); Assert.False(second.HasStatus("mv"));
        Assert.Equal(1, first.Movement.PendingCount); Assert.Equal(0, second.Movement.PendingCount);
        ExecutorTick();
        Assert.Equal((1, 1), (first.X, first.Y));
        Assert.Equal((2, 1), (second.X, second.Y));
        for (var cycle = 0; cycle < 12; cycle++) ExecutorTick();
        Assert.False(second.Movement.HasIntent);
        Assert.False(second.HasStatus("mv"));
    }

    [Fact]
    public async Task NativeFastWalkClickKeepsTheHorseAsAPassenger()
    {
        var rider = ExecutorActor(0, 1);
        var horse = ExecutorAdditionalBot(0, 1, 2); ExecutorTick();
        rider.RidingHorse = horse.RidingHorse = true;
        rider.HorseId = horse.VirtualId; horse.HorseId = rider.VirtualId;
        rider.FastWalking = true;
        await new Plus.Communication.Packets.Incoming.Rooms.Engine.MoveAvatarEvent()
            .Parse(_client, ClientPacket(3, 1));
        ExecutorTick();
        Assert.Equal("2,1,1", rider.Statusses["mv"]);
        Assert.Equal("2,1,0", horse.Statusses["mv"]);
        Assert.Equal(0, horse.Movement.PendingCount);
        Assert.False(horse.Movement.HasIntent);
        ExecutorTick();
        Assert.Equal((2, 1, 1d), (rider.X, rider.Y, rider.Z));
        Assert.Equal((2, 1, 0d), (horse.X, horse.Y, horse.Z));
    }

    [Fact]
    public void AnnounceServiceMountExcludesItsHorseAndMirrorsHorseMovement()
    {
        var rider = ExecutorActor(0, 1);
        var horse = ExecutorAdditionalBot(0, 1, 2); ExecutorTick();
        rider.RidingHorse = horse.RidingHorse = true;
        rider.HorseId = horse.VirtualId; horse.HorseId = rider.VirtualId;
        rider.MoveTo(2, 1); ExecutorTick();
        Assert.Equal("1,1,1", rider.Statusses["mv"]);
        Assert.Equal("1,1,0", horse.Statusses["mv"]);
        ExecutorTick();
        Assert.Equal((1, 1, 1d), (rider.X, rider.Y, rider.Z));
        Assert.Equal((1, 1, 0d), (horse.X, horse.Y, horse.Z));
        ExecutorTick();
        Assert.Equal((2, 1), (rider.X, rider.Y)); Assert.Equal((2, 1), (horse.X, horse.Y));
        Assert.False(rider.HasStatus("mv")); Assert.False(horse.HasStatus("mv"));
    }

    [Fact]
    public void CommitServiceTeleportingWalkOnHookAbortsTheOldLandingAndLaterPhases()
    {
        var landing = ExecutorFloor(10, 1, 1);
        var destination = ExecutorFloor(11, 3, 2, z: .75);
        var actor = ExecutorActor(0, 1);
        var calls = 0;
        ExecutorObserveLanding((user, item) =>
        {
            if (item != landing) return;
            calls++; _room.GetGameMap().TeleportToItem(user, destination);
        });
        actor.MoveTo(2, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal(1, calls); Assert.Equal((3, 2, .75), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("mv")); Assert.False(actor.Movement.HasIntent);
        Assert.Equal(destination.Id, actor.Movement.CurrentRef!.Value.SupportItemId);
    }

    [Fact]
    public void CommitServiceFurnitureOnlyWalkOnHookDoesNotAbortLanding()
    {
        var landing = ExecutorFloor(10, 1, 1);
        var adjacent = ExecutorFloor(11, 2, 1);
        var actor = ExecutorActor(0, 1);
        var revision = actor.Movement.LocationRevision;
        ExecutorObserveLanding((_, item) =>
        {
            if (item == landing) Assert.True(_room.GetRoomItemHandler().SetFloorItem(adjacent, 2, 1, .5));
        });
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(revision, actor.Movement.LocationRevision);
        Assert.False(actor.HasStatus("mv")); Assert.Same(landing, actor.LastItem);
    }

    private RoomUser ExecutorConfiguredActor(PathfindingSettings settings)
    {
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, settings);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);
        foreach (var item in _room.GetRoomItemHandler().GetFloor) navigation.Inputs.Attach(item);
        var actor = Viewer(0, 1); actor.InternalRoomId = actor.VirtualId; actor.UserId = 7;
        navigation.Admit(actor); ExecutorTick();
        return actor;
    }

    private RoomUser ExecutorAdditionalBot(int x, int y, int id)
    {
        var actor = new RoomUser(0, RoomId, id, _room) { X = x, Y = y, InternalRoomId = id };
        actor.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        actor.BotData.AiType = BotAiType.Generic;
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(users.TryAdd(id, actor));
        _room.GetGameMap().Navigation!.Admit(actor);
        return actor;
    }

    private void ExecutorObserveLanding(Action<RoomUser, Item> action)
    {
        var item = Furni(900, InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni);
        item.Definition.Height = 0; item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));
        Assert.True(_room.GetWired().AddBox(new MovementLandingObserver(_room, item, action)));
    }

    private sealed class MovementLandingObserver(Room room, Item item, Action<RoomUser, Item> action) : IWiredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.TriggerWalkOnFurni;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments)
        {
            var actor = Instance.GetRoomUserManager().GetRoomUserByHabbo(((Plus.HabboHotel.Users.Habbo)arguments[0]).Id);
            action(actor, (Item)arguments[1]); return false;
        }
    }
}
