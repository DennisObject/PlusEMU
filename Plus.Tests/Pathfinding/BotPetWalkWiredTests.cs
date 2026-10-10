using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    public enum WalkActorKind
    {
        Habbo, Bot, Pet
    }

    [Theory]
    [InlineData(WalkActorKind.Bot, false)]
    [InlineData(WalkActorKind.Bot, true)]
    [InlineData(WalkActorKind.Pet, false)]
    [InlineData(WalkActorKind.Pet, true)]
    [InlineData(WalkActorKind.Habbo, false)]
    [InlineData(WalkActorKind.Habbo, true)]
    public void PickedWalkTriggersFireForEveryAvatarKind(WalkActorKind kind, bool v2)
    {
        var origin = ExecutorFloor(10, 0, 1);
        var landing = ExecutorFloor(11, 1, 1);
        var fired = new List<(WiredEventKind Kind, RoomUser? Actor, Item? Item)>();
        WalkObserveModern(20, 3, 0, "wf_trg_walks_off_furni", origin, fired);
        WalkObserveModern(22, 2, 3, "wf_trg_walks_on_furni", landing, fired);
        var legacy = ExecutorWalkEvents();
        var actor = WalkActor(kind, 0, 1, v2);

        actor.MoveTo(1, 1);
        ExecutorTick();
        ExecutorTick();
        ExecutorTick();

        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal(new[] { (WiredEventKind.WalkOff, origin.Id), (WiredEventKind.WalkOn, landing.Id) },
            fired.Select(x => (x.Kind, x.Item!.Id)).ToArray());
        Assert.All(fired, x => Assert.Same(actor, x.Actor));
        // Legacy walk boxes take a Habbo argument and stay Habbo-only.
        Assert.Equal(kind == WalkActorKind.Habbo, legacy.Count > 0);
    }

    [Theory]
    [InlineData(WalkActorKind.Bot)]
    [InlineData(WalkActorKind.Pet)]
    public void BotAndPetWalksOnUnpickedFurniDoNotFireOrTrackLastItem(WalkActorKind kind)
    {
        var picked = ExecutorFloor(10, 3, 1);
        ExecutorFloor(11, 1, 1);
        var fired = new List<(WiredEventKind Kind, RoomUser? Actor, Item? Item)>();
        WalkObserveModern(22, 3, 0, "wf_trg_walks_on_furni", picked, fired);
        var actor = WalkActor(kind, 0, 1, v2: true);

        actor.MoveTo(1, 1);
        ExecutorTick();
        ExecutorTick();

        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Empty(fired);
        Assert.Null(actor.LastItem);
    }

    private RoomUser WalkActor(WalkActorKind kind, int x, int y, bool v2)
    {
        RoomUser actor;

        if (kind == WalkActorKind.Habbo) {
            actor = Viewer(x, y);
            actor.UserId = 7;
        }
        else {
            var bot = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
            bot.AiType = kind == WalkActorKind.Pet ? BotAiType.Pet : BotAiType.Generic;
            bot.Name = kind.ToString();
            actor = new(0, RoomId, 5, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = x, Y = y, BotData = bot };
            var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
            users[actor.VirtualId] = actor;
        }

        Assert.Equal(kind != WalkActorKind.Habbo, actor.IsBot);
        Assert.Equal(kind == WalkActorKind.Pet, actor.IsPet);

        if (!v2) {
            _room.GetGameMap().AddUserToMap(actor, actor.Coordinate);

            return actor;
        }

        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, new() { Engine = PathfindingEngine.V2 },
            TestLogging.Navigation, new TestGroupManager(id => _groupLookup(id)), _database, TestNavigationRewards.Instance);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);

        foreach (var item in _room.GetRoomItemHandler().GetFloor) {
            navigation.Inputs.Attach(item);
        }

        actor.InternalRoomId = actor.VirtualId;
        navigation.Admit(actor);
        ExecutorTick();

        return actor;
    }

    private void WalkObserveModern(uint id, int x, int y, string trigger, Item picked,
        List<(WiredEventKind Kind, RoomUser? Actor, Item? Item)> fired)
    {
        var triggerItem = Furni(id, InteractionType.WiredTrigger, WiredBoxType.None);
        var actionItem = Furni(id + 1, InteractionType.WiredEffect, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, triggerItem, x, y, 0, true, false, false));
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, actionItem, x, y, 0, true, false, false));
        Assert.True(WiredBoxRegistry.TryGet(trigger, out var descriptor));
        var box = new WiredModernTrigger(_room, triggerItem, descriptor);
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, WiredTriggerConfiguration.Defaults(trigger) with { SelectedItems = [picked.Id] },
            out var config, out var error), error);
        box.ApplyConfiguration(config);
        Assert.True(_room.GetWired().AddBox(box));
        Assert.True(_room.GetWired().AddBox(new WalkRecordingAction(_room, actionItem, fired)));
    }

    private sealed class WalkRecordingAction(Room room, Item item, List<(WiredEventKind Kind, RoomUser? Actor, Item? Item)> fired)
        : IWiredContextualAction
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.None;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public WiredBoxDescriptor Descriptor { get; } = new("test", WiredBoxCategory.Action, 0, "test") { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; private set; } = new();
        public bool IsNegative => false;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments) => throw new InvalidOperationException("Modern box requires context");
        public bool Execute(WiredRuntimeContext context)
        {
            fired.Add((context.Event.Kind, context.Event.Actor, context.Event.EventItem));

            return true;
        }
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = "";

            return true;
        }
        public void ApplyConfiguration(WiredConfiguration validated) => Configuration = validated;
    }
}
