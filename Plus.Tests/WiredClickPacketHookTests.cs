using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public class WiredClickPacketHookTests
{
    [Theory]
    [InlineData(-5, 10, 4294967291u, false)]
    [InlineData(-5, 20, 5u, true)]
    [InlineData(int.MinValue, 20, 2147483648u, true)]
    public async Task FurnitureClickDecodesTheCompleteFrameBeforeOneDomainCall(int wireId, int category, uint itemId, bool isWall)
    {
        var service = new RecordingFurniture();
        await new ClickFurniEvent(service).Parse(null!, null!, Packet(wireId, category));
        Assert.Equal(new FurnitureClickRequest(itemId, isWall), Assert.Single(service.Clicks));
    }

    [Fact]
    public async Task MalformedFurnitureClicksDoNotDelegateOrInspectRoomState()
    {
        var service = new RecordingFurniture();
        var handler = new ClickFurniEvent(service);

        foreach (var values in new[] { Array.Empty<int>(), new[] { 1 }, new[] { 0, 10 }, new[] { 1, 0 }, new[] { 1, 10, 9 } }) {
            await handler.Parse(null!, null!, Packet(values));
        }

        Assert.Empty(service.Clicks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ClickUserCarriesExactActorAndTargetAndOnlyAcceptedSettings(bool blockMenu, bool noRotate)
    {
        var world = new World("wf_trg_click_user", [blockMenu ? 1 : 0, noRotate ? 1 : 0]);
        await new ClickUserEvent().Parse(world.Room, world.Client, Packet(world.Target.VirtualId));
        var evt = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, evt.Actor);
        Assert.Same(world.Target, evt.TargetUser);
        var response = Assert.Single(world.Packets, p => p.Id == 9460).Payload;
        Assert.Equal(world.Target.VirtualId, response.ReadInt());
        Assert.Equal(!blockMenu, response.ReadBool());
        var links = world.Packets.Where(p => p.Id == 2023).Select(p => p.Payload.ReadString()).ToArray();
        Assert.Equal(noRotate, links.Contains("avatar-info/block-rotate"));
        Assert.Equal(blockMenu, links.Contains("avatar-info/block-menu"));
    }

    [Fact]
    public async Task RejectedConditionCannotBlockMenuOrRotation()
    {
        var world = new World("wf_trg_click_user", [1, 1]);
        world.AddBox("wf_cnd_user_count_in", [10, 20, 0]);
        await new ClickUserEvent().Parse(world.Room, world.Client, Packet(world.Target.VirtualId));
        Assert.Empty(world.Capture.Events);
        Assert.DoesNotContain(world.Packets, p => p.Id == 2023);
        var response = Assert.Single(world.Packets, p => p.Id == 9460).Payload;
        Assert.Equal(world.Target.VirtualId, response.ReadInt());
        Assert.True(response.ReadBool());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BotAndPetTargetsAreNotHumanAvatarClicks(bool pet)
    {
        var world = new World("wf_trg_click_user", [1, 1]);
        world.Target.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        world.Target.BotData.AiType = pet ? BotAiType.Pet : BotAiType.Generic;
        await new ClickUserEvent().Parse(world.Room, world.Client, Packet(world.Target.VirtualId));
        Assert.Empty(world.Capture.Events);
        Assert.Empty(world.Packets);
    }

    [Fact]
    public async Task ActualRegistryTemporaryFloorClickDispatchesOnlyTheClickAndKeepsSameState()
    {
        var world = new World("wf_trg_click_furni", [0]);
        var definition = new ItemDefinition
        {
            Id = 30,
            ItemName = "template",
            Type = ItemType.Floor,
            Length = 1,
            Width = 1,
            Height = 0.1,
            Stackable = true,
            Walkable = true
        };
        var item = world.Room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 42, 1, 1, 0, 0, "unchanged")!;
        Assert.True(world.Room.GetRoomItemHandler().OwnsTemporary(item));
        world.Packets.Clear();
        await new ClickFurniEvent(new FurnitureUseService(null!, null!)).Parse(world.Room, world.Client, Packet(unchecked((int)item.Id), 10));
        var evt = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, evt.Actor);
        Assert.Same(item, evt.EventItem);
        Assert.Equal(WiredEventKind.ClickFurni, evt.Kind);
        Assert.Equal("unchanged", item.LegacyDataString);
        Assert.True(world.Room.GetRoomItemHandler().OwnsTemporary(item));
        Assert.Empty(world.Packets); // No interactor, inventory, ordinary-use, or persistence path.
        world.Capture.Events.Clear();
        var lookalike = new Item { Id = item.Id, IsTemporary = true, Definition = definition };
        world.Items("_floorItems")[item.Id] = lookalike;
        await new ClickFurniEvent(new FurnitureUseService(null!, null!)).Parse(world.Room, world.Client, Packet(unchecked((int)item.Id), 10));
        Assert.Empty(world.Capture.Events); // The registry owns the original reference, not an item with matching bits.
    }

    [Fact]
    public async Task NegativeWallMagnitudeResolvesWallWhileSameFloorWireBitsStayDistinct()
    {
        var world = new World("wf_trg_click_furni", [0]);
        var wall = new Item { Id = 5, Definition = new() { Type = ItemType.Wall } };
        world.Items("_wallItems").TryAdd(5, wall);
        await new ClickFurniEvent(new FurnitureUseService(null!, null!)).Parse(world.Room, world.Client, Packet(-5, 20));
        Assert.Same(wall, Assert.Single(world.Capture.Events).EventItem);
        world.Capture.Events.Clear();
        await new ClickFurniEvent(new FurnitureUseService(null!, null!)).Parse(world.Room, world.Client, Packet(-5, 10));
        Assert.Empty(world.Capture.Events);
        var highFloor = new Item { Id = unchecked((uint)-5), Definition = new() { Type = ItemType.Floor } };
        world.Items("_floorItems").TryAdd(highFloor.Id, highFloor);
        await new ClickFurniEvent(new FurnitureUseService(null!, null!)).Parse(world.Room, world.Client, Packet(-5, 10));
        Assert.Same(highFloor, Assert.Single(world.Capture.Events).EventItem);
        Assert.False(highFloor.IsTemporary);
    }

    [Fact]
    public async Task TileClickFiresForValidFrozenActorAndInvisibleTileItemWithoutWalking()
    {
        var world = new World("wf_trg_click_tile", [0]);
        var item = new Item
        {
            Id = 5,
            GetX = 1,
            GetY = 1,
            Definition = new()
            {
                Type = ItemType.Floor,
                Length = 1,
                Width = 1,
                InteractionName = "room_invisible_click_tile"
            }
        };
        world.Items("_floorItems").TryAdd(5, item);
        // The background click trigger's selected furnishing defines its tile.
        world.AddBox("wf_trg_click_tile", [100], [5]);
        world.Actor.CanWalk = false;
        await new MoveAvatarEvent(new RoomAvatarActionService(TimeProvider.System, null!, null!)).Parse(world.Client, Packet(1, 1));
        Assert.Equal((0, 0), (world.Actor.X, world.Actor.Y));
        Assert.Equal((1, 1), (Assert.Single(world.Capture.Events).X, world.Capture.Events[0].Y));
        world.Capture.Events.Clear();
        await new MoveAvatarEvent(new RoomAvatarActionService(TimeProvider.System, null!, null!)).Parse(world.Client, Packet(-1, 1));
        Assert.Empty(world.Capture.Events);
        await new ClickFurniEvent(new FurnitureUseService(null!, null!)).Parse(world.Room, world.Client, Packet(5, 10));
        Assert.All(world.Capture.Events, evt => Assert.Equal(WiredEventKind.ClickTile, evt.Kind));
        Assert.NotEmpty(world.Capture.Events);
        Assert.Same(item, world.Capture.Events[0].EventItem);
    }

    [Fact]
    public async Task DelayedClickFiringCannotTransferToReplacementActorWithSameVirtualAndHabboIds()
    {
        var world = new World("wf_trg_click_user", [0, 0]);
        world.Capture.ApplyConfiguration(new() { Delay = 1 });
        await new ClickUserEvent().Parse(world.Room, world.Client, Packet(world.Target.VirtualId));
        Assert.Empty(world.Capture.Events);
        var replacement = new RoomUser(world.Actor.HabboId, 1, world.Actor.VirtualId, world.Room, world.Client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        ((ConcurrentDictionary<int, RoomUser>)Get(world.Room.GetRoomUserManager(), "_users"))[world.Actor.VirtualId] = replacement;
        world.Clock = 1000;
        world.Room.GetWired().OnCycle();
        Assert.Empty(world.Capture.Events);
    }

    [Fact]
    public void ActualClickHandlersAndResponseMappingsAreUnique()
    {
        IPacketEvent[] handlers = [new ClickFurniEvent(new FurnitureUseService(null!, null!)), new ClickUserEvent()];
        using var manager = new PacketManager(handlers, NullLogger<PacketManager>.Instance);
        var registered = (Dictionary<uint, IPacketEvent>)Get(manager, "_incomingPackets");
        var revision = new Plus.Communication.Revisions.RevisionsCache().InternalRevision;

        foreach (var handler in handlers) {
            var name = handler.GetType().Name;
            var id = (uint)typeof(ClientPacketHeader).GetField(name)!.GetRawConstantValue()!;
            Assert.Same(handler, registered[id]);
            var wire = revision.IncomingHeaders[name];
            Assert.Equal(id, revision.IncomingIdToInternalIdMapping[wire]);
            Assert.Single(revision.IncomingHeaders, pair => pair.Value == wire);
        }

        foreach (var name in new[] { nameof(ServerPacketHeader.WiredClickUserResponseComposer), nameof(ServerPacketHeader.InClientLinkComposer) }) {
            var id = (uint)typeof(ServerPacketHeader).GetField(name)!.GetRawConstantValue()!;
            var wire = revision.OutgoingHeaders[name];
            Assert.Equal(wire, revision.InternalIdToOutgoingIdMapping[id]);
            Assert.Single(revision.OutgoingHeaders, pair => pair.Value == wire);
        }
    }

    private sealed class RecordingFurniture : IFurnitureUseService
    {
        public List<FurnitureClickRequest> Clicks { get; } = [];
        public void Click(Room room, GameClient session, FurnitureClickRequest request) => Clicks.Add(request);
        public void Use(Room room, GameClient session, FurnitureUseRequest request) => throw new NotSupportedException();
        public void TurnOffDice(Room room, GameClient session, uint itemId) => throw new NotSupportedException();
        public void RollDice(Room room, GameClient session, FurnitureUseRequest request) => throw new NotSupportedException();
        public void UseOneWayGate(Room room, GameClient session, uint itemId) => throw new NotSupportedException();
        public void UseWall(Room room, GameClient session, FurnitureUseRequest request) => throw new NotSupportedException();
    }

    private static FlashIncomingPacket Packet(params int[] values)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = new FlashOutgoingPacket(stream);

        foreach (var value in values) {
            packet.WriteInteger(value);
        }

        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }

    private sealed class World
    {
        public Room Room { get; }
        public FlashGameClient Client { get; }
        public RoomUser Actor { get; }
        public RoomUser Target { get; }
        public CaptureAction Capture { get; }
        public List<(uint Id, FlashIncomingPacket Payload)> Packets { get; } = [];
        public long Clock;
        private readonly WiredComponent _wired;
        private uint _next = 10;
        public World(string trigger, int[] parameters)
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            Room.Id = 1;
            Set(Room, "_interactionClock", TimeProvider.System);
            var items = new RoomItemHandling(Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
            var users = new RoomUserManager(Room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
            Set(Room, "_roomItemHandling", items);
            Set(Room, "_roomUserManager", users);
            var map = new Gamemap(Room, new RoomModel("click-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, true), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            Set(Room, "_gamemap", map);
            typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[3, 3]);
            typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[3, 3]);
            _wired = new WiredComponent(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            Set(Room, "_wiredComponent", _wired);
            Set(Get(_wired, "_engine"), "_now", (Func<long>)(() => Clock));
            Client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new()
                {
                    InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                    {
                        [ServerPacketHeader.WiredClickUserResponseComposer] = 9460,
                        [ServerPacketHeader.InClientLinkComposer] = 2023,
                        [ServerPacketHeader.ObjectAddComposer] = 1534,
                        [ServerPacketHeader.ObjectRemoveComposer] = 2703
                    }
                },
                SendCallback = args =>
                {
                    Packets.Add(((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2)),
                    new() { Buffer = args.MemoryBuffer[6..].ToArray() }));

                    return true;
                }
            };
            Actor = AddUser(42, 7, Client);
            var targetClient = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient) { Revision = Client.Revision, SendCallback = _ => true };
            Target = AddUser(43, 8, targetClient);
            Capture = new CaptureAction(Room, Item("wf_act_toggle_state"));
            Assert.True(_wired.AddBox(Capture));
            AddBox(trigger, parameters);
        }
        private RoomUser AddUser(int habboId, int virtualId, FlashGameClient client)
        {
            client.SetHabbo(new Habbo { Id = habboId, Username = "actor" + habboId, CurrentRoom = Room, Client = client });
            var user = new RoomUser(habboId, 1, virtualId, Room, client, TestChatEmotions.Unused, TestRewardProgress.Unused);
            ((ConcurrentDictionary<int, RoomUser>)Get(Room.GetRoomUserManager(), "_users")).TryAdd(virtualId, user);

            return user;
        }
        public IWiredConfiguredItem AddBox(string name, int[] parameters, uint[]? selected = null)
        {
            var box = _wired.CreateConfiguredBox(Item(name))!;
            Assert.True(box.TryValidateConfiguration(new() { IntParams = [.. parameters], SelectedItems = selected == null ? [] : [.. selected] }, out var config, out var error), error);
            box.ApplyConfiguration(config);
            Assert.True(_wired.AddBox(box));

            return box;
        }
        private Item Item(string name)
        {
            var item = new Item { Id = _next++, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new() { ItemName = name, Type = ItemType.Floor } };
            Items("_floorItems").TryAdd(item.Id, item);

            return item;
        }
        public ConcurrentDictionary<uint, Item> Items(string field) => (ConcurrentDictionary<uint, Item>)Get(Room.GetRoomItemHandler(), field);
    }
    private sealed class CaptureAction(Room room, Item item) : WiredModernBox(room, item,
        WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_toggle_state")), IWiredContextualAction
    {
        public List<WiredRuntimeEvent> Events { get; } = [];
        public bool IsNegative => false;
        public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = "";

            return true;
        }
        public override bool Execute(WiredRuntimeContext context)
        {
            Events.Add(context.Event);

            return true;
        }
    }
    private static object Get(object value, string field) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
}
