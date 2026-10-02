using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public sealed class WiredNativeLifecycleTests
{
    [Fact]
    public void NativeWakeFiresOnceThroughConfiguredTriggerAndRoomEngine()
    {
        var f = new World(); var actor = f.Bot();
        var action = f.Action(WiredAvatarAction.Awake);
        actor.IsAsleep = true;
        actor.UnIdle(); f.Wired.OnFastCycle();
        Assert.Equal(1, action.Calls);
        actor.UnIdle(); f.Wired.OnFastCycle();
        Assert.Equal(1, action.Calls);
    }

    [Fact]
    public void NativeBedPoseFiresOnceThroughConfiguredTriggerAndRoomEngine()
    {
        var f = new World(); var actor = f.Bot();
        var action = f.Action(WiredAvatarAction.Lay);
        var bed = f.Item(3);
        bed.Definition.InteractionType = InteractionType.Bed;
        bed.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        f.Map.AddToMap(bed);
        actor.X = 1; actor.Y = 1;
        f.Room.GetRoomUserManager().UpdateUserStatus(actor, false); f.Wired.OnFastCycle();
        Assert.True(actor.Statusses.ContainsKey("lay"));
        Assert.Equal(1, action.Calls);
        f.Room.GetRoomUserManager().UpdateUserStatus(actor, false); f.Wired.OnFastCycle();
        Assert.Equal(1, action.Calls);
    }

    [Fact]
    public void NativeRemovalDispatchesLeaveAfterVisitDetached()
    {
        var f = new World(); var actor = f.Bot();
        var item = f.Item(1); item.Definition.InteractionName = "wf_trg_leave_room";
        var trigger = f.Wired.CreateConfiguredBox(item)!; Assert.True(f.Wired.AddBox(trigger));
        var effectItem = f.Item(2); effectItem.Definition.InteractionType = InteractionType.WiredEffect;
        var effect = new CounterAction(f.Room, effectItem); Assert.True(f.Wired.AddBox(effect));
        typeof(RoomUserManager).GetMethod("RemoveRoomUser", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Room.GetRoomUserManager(), [actor]);
        Assert.Null(f.Room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId));
        f.Wired.OnFastCycle(); Assert.Equal(1, effect.Calls);
    }

    [Theory]
    [InlineData("wf_upcounter1")]
    [InlineData("wf_upcounter2")]
    [InlineData("wf_game_upcounter1")]
    [InlineData("wf_game_upcounter2")]
    public void NativeCounterInteractorRetainsStateAndStartsOnlyWithRights(string name)
    {
        var f = new World(); var item = f.Item(1);
        item.Definition.InteractionName = name; item.Definition.ItemName = name;
        item.Definition.InteractionType = InteractionTypes.GetTypeFromString(name);
        Assert.Equal(InteractionType.Counter, item.Definition.InteractionType);
        item.LegacyDataString = "3";
        var interactor = new InteractorCounter();
        interactor.OnPlace(null!, item);
        Assert.Equal("3", item.LegacyDataString);
        interactor.OnTrigger(null!, item, 0, false);
        Assert.False(f.Wired.NeedsFastCycle);
        interactor.OnTrigger(null!, item, 0, true);
        Assert.True(f.Wired.NeedsFastCycle);
        interactor.OnTrigger(null!, item, 0, true);
        f.Wired.OnFastCycle(); // Deliver the actual GameEnd event before the room becomes idle.
        Assert.False(f.Wired.NeedsFastCycle);
    }

    private sealed class World
    {
        public Room Room { get; } = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        public Gamemap Map { get; }
        public WiredComponent Wired { get; }
        private readonly ConcurrentDictionary<uint, Item> _items;
        private readonly ConcurrentDictionary<int, RoomUser> _users;
        public World()
        {
            Room.Id = 1;
            Map = new(Room, new RoomModel("wired-test", 0, 0, 0, 0, "000\r000\r000", false, 0, true));
            var handler = new RoomItemHandling(Room);
            Set(Room, "_gamemap", Map); Set(Room, "_roomItemHandling", handler);
            var users = new RoomUserManager(Room); Set(Room, "_roomUserManager", users);
            typeof(Gamemap).GetProperty("GameMap")!.SetValue(Map, new byte[3, 3]);
            typeof(Gamemap).GetProperty("EffectMap")!.SetValue(Map, new byte[3, 3]);
            _items = (ConcurrentDictionary<uint, Item>)Get(handler, "_floorItems");
            _users = (ConcurrentDictionary<int, RoomUser>)Get(users, "_users");
            Wired = new(Room); Set(Room, "_wiredComponent", Wired);
        }
        public RoomUser Bot()
        {
            var user = new RoomUser(0, 1, 7, Room) { BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot)), InternalRoomId = 7 };
            _users[user.VirtualId] = user; return user;
        }
        public Item Item(uint id)
        {
            var item = new Item { Id = id, RoomId = 1, ExtraData = new LegacyDataFormat { Data = "0" },
                Definition = new() { Type = ItemType.Floor, Width = 1, Length = 1, Modes = 2,
                    ItemName = "test", PublicName = "test", AdjustableHeights = [], VendingIds = [] } };
            Set(item, "_room", Room); _items[id] = item; return item;
        }
        public CounterAction Action(WiredAvatarAction kind)
        {
            var triggerItem = Item(1); triggerItem.Definition.InteractionName = "wf_trg_user_performs_action";
            var trigger = Wired.CreateConfiguredBox(triggerItem)!;
            Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [(int)kind, 0, 0, 0, 1] }, out var config, out var error), error);
            trigger.ApplyConfiguration(config); Assert.True(Wired.AddBox(trigger));
            var item = Item(2); item.Definition.InteractionType = InteractionType.WiredEffect;
            var action = new CounterAction(Room, item); Assert.True(Wired.AddBox(action)); return action;
        }
    }
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
    private static object Get(object owner, string field) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private sealed class CounterAction(Room room, Item item) : IWiredContextualAction
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.EffectShowMessage;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public int Calls;
        public WiredBoxDescriptor Descriptor { get; } = new("test", WiredBoxCategory.Action, 0, 0, "test") { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; private set; } = new();
        public bool IsNegative => false;
        public bool Execute(params object[] arguments) => throw new NotSupportedException();
        public bool Execute(WiredRuntimeContext context) { Calls++; return true; }
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        { validated = proposed; error = ""; return true; }
        public void ApplyConfiguration(WiredConfiguration validated) => Configuration = validated;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
    }
}
