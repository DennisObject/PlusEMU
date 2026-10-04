using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Xunit;

namespace Plus.Tests;

public class WiredAvatarPacketHookTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(2, 2)] [InlineData(3, 3)] [InlineData(5, 5)] [InlineData(7, 11)]
    public async Task ExpressionPacketRunsActualConfiguredTriggerOnceWithActorIdentity(int expression, int editorAction)
    {
        var world = new World(editorAction);
        await new ActionEvent(new NoQuests()).Parse(world.Room, world.Client, Packet(expression));
        var observed = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, observed.Actor); Assert.Equal(editorAction, observed.Action); Assert.Equal(-1, observed.Code);
    }

    [Theory]
    [InlineData(0)] [InlineData(17)]
    public async Task SignPacketRunsExactSignFilterAndRejectsOutOfRangeValue(int sign)
    {
        var world = new World(9, sign);
        await new ApplySignEvent().Parse(world.Room, world.Client, Packet(sign));
        var observed = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, observed.Actor); Assert.Equal(sign, observed.Code);
        Assert.Equal(sign.ToString(), world.Actor.Statusses["sign"]);
        await new ApplySignEvent().Parse(world.Room, world.Client, Packet(18));
        Assert.Single(world.Capture.Events); Assert.Equal(sign.ToString(), world.Actor.Statusses["sign"]);
    }

    [Theory]
    [InlineData(1)] [InlineData(4)]
    public async Task DancePacketCarriesActualDanceCodeAndStopDoesNotFire(int dance)
    {
        var world = new World(10, dance);
        world.Client.GetHabbo().Access = Plus.HabboHotel.Permissions.UserAccess.Create([], [new(Plus.HabboHotel.Permissions.PermissionKeys.ClubAccess, false)]);
        var handler = new DanceEvent(new NoQuests());
        await handler.Parse(world.Room, world.Client, Packet(dance));
        var observed = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, observed.Actor); Assert.Equal(dance, observed.Code);
        await handler.Parse(world.Room, world.Client, Packet(0));
        Assert.Single(world.Capture.Events); Assert.Equal(0, world.Actor.DanceId);
    }

    [Fact]
    public async Task CurrentPostureRequestsFireOnlyActualSitAndStandTransitions()
    {
        var world = new World(6);
        world.AddTrigger(7);
        var handler = new SitEvent();
        await handler.Parse(world.Client, Packet(1)); await handler.Parse(world.Client, Packet(1));
        Assert.True(world.Actor.IsSitting); Assert.Single(world.Capture.Events);
        await handler.Parse(world.Client, Packet(0)); await handler.Parse(world.Client, Packet(0));
        Assert.False(world.Actor.IsSitting); Assert.Equal(0, world.Actor.Z);
        Assert.Equal(new[] { 6, 7 }, world.Capture.Events.Select(evt => evt.Action));
        Assert.All(world.Capture.Events, evt => Assert.Same(world.Actor, evt.Actor));
    }

    private static FlashIncomingPacket Packet(int value)
    {
        using var stream = PlusMemoryStream.GetStream(); var packet = new FlashOutgoingPacket(stream);
        packet.WriteInteger(value); return new() { Buffer = stream.ToArray().AsMemory(6) };
    }

    private sealed class World
    {
        public Room Room { get; }
        public FlashGameClient Client { get; }
        public RoomUser Actor { get; }
        public CaptureAction Capture { get; }
        private readonly WiredComponent _wired;
        private uint _next = 10;
        public World(int action, int code = -1)
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); Room.Id = 1;
            var items = new RoomItemHandling(Room, TestRoomItemStore.Instance); var users = new RoomUserManager(Room);
            Set(Room, "_roomItemHandling", items); Set(Room, "_roomUserManager", users);
            _wired = new WiredComponent(Room, TestLogging.Logger); Set(Room, "_wiredComponent", _wired);
            Client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                { [ServerPacketHeader.ActionComposer] = 1, [ServerPacketHeader.DanceComposer] = 2,
                    [ServerPacketHeader.SleepComposer] = 3, [ServerPacketHeader.AvatarEffectComposer] = 4 } },
                SendCallback = _ => true
            };
            Client.SetHabbo(new Habbo { Id = 42, Username = "actor", CurrentRoom = Room, Client = Client, Effects = new EffectsComponent() });
            Actor = new RoomUser(42, 1, 7, Room); Set(Actor, "_mClient", Client);
            ((ConcurrentDictionary<int, RoomUser>)Get(users, "_users")).TryAdd(7, Actor);
            var captureItem = Item("wf_act_toggle_state");
            Capture = new CaptureAction(Room, captureItem);
            Assert.True(_wired.AddBox(Capture));
            AddTrigger(action, code);
        }

        public void AddTrigger(int action, int code = -1)
        {
            var item = Item("wf_trg_user_performs_action");
            var trigger = _wired.CreateConfiguredBox(item)!;
            var parameters = new[] { action, action == 9 ? 1 : 0, action == 9 ? code : 0, action == 10 ? 1 : 0, action == 10 ? code : 1 };
            Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [.. parameters] }, out var config, out var error), error);
            trigger.ApplyConfiguration(config); Assert.True(_wired.AddBox(trigger));
        }

        private Item Item(string name)
        {
            var item = new Item { Id = _next++, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new() { ItemName = name } };
            ((ConcurrentDictionary<uint, Item>)Get(Room.GetRoomItemHandler(), "_floorItems")).TryAdd(item.Id, item);
            return item;
        }
    }

    private sealed class CaptureAction(Room room, Item item) : WiredModernBox(room, item,
        WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_toggle_state")), IWiredContextualAction
    {
        public List<WiredRuntimeEvent> Events { get; } = [];
        public bool IsNegative => false;
        public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        { validated = proposed; error = ""; return true; }
        public override bool Execute(WiredRuntimeContext context) { Events.Add(context.Event); return true; }
    }

    private sealed class NoQuests : IQuestManager
    {
        public void ProgressUserQuest(GameClient session, QuestType type, int data = 0) { }
        public void Init() => throw new NotSupportedException();
        public Quest GetQuest(int id) => throw new NotSupportedException();
        public int GetAmountOfQuestsInCategory(string category) => throw new NotSupportedException();
        public Quest GetNextQuestInSeries(string category, int number) => throw new NotSupportedException();
        public void GetList(GameClient session, ClientPacket message) => throw new NotSupportedException();
        public void QuestReminder(GameClient session, int questId) => throw new NotSupportedException();
    }

    private static object Get(object value, string field) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
}
