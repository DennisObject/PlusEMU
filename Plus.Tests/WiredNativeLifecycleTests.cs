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
using Plus.HabboHotel;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.WiredVariables;
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

    [Theory]
    [InlineData("wf_antenna1")]
    [InlineData("wf_antenna2")]
    public void NativeAntennaDeliversConfiguredSignalAndRejectsDetachedReceiver(string name)
    {
        var f = new World();
        var antenna = f.Item(3); antenna.Definition.ItemName = name; antenna.Definition.InteractionName = "antenna";
        var triggerItem = f.Item(1); triggerItem.Definition.InteractionName = "wf_trg_recv_signal";
        var trigger = f.Wired.CreateConfiguredBox(triggerItem)!;
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [0, 100], SelectedItems = [antenna.Id] }, out var config, out var error), error);
        trigger.ApplyConfiguration(config); Assert.True(f.Wired.AddBox(trigger));
        var effect = f.Effect();
        var context = f.Context();
        Assert.True(f.Wired.SendSignal(context, [antenna], new()));
        f.Wired.OnFastCycle(); Assert.Equal(1, effect.Calls);
        f.Remove(antenna);
        Assert.False(f.Wired.SendSignal(context, [antenna], new()));
    }

    [Fact]
    public void NativeBuiltinChangeReachesTypedVariableTriggerThroughRoomQueue()
    {
        var f = new World(); var actor = f.Bot(); f.PrepareVariables();
        var item = f.Item(1); item.Definition.InteractionName = "wf_trg_var_changed";
        var trigger = f.Wired.CreateConfiguredBox(item)!;
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [0, 0, 1, 1, 1, 1, 0, -1], Text = "internal:@handitem" }, out var config, out var error), error);
        trigger.ApplyConfiguration(config); Assert.True(f.Wired.AddBox(trigger));
        var effect = f.Effect(); var context = f.Context();
        var frame = WiredVariableRuntimeFrames.Create(context);
        Assert.True(f.Wired.Variables.Module.Mutate(new(WiredVariableTarget.User, "internal:@handitem"),
            WiredVariableRuntimeFrames.UserHolder(actor), WiredVariableMutation.Set, 7, frame));
        f.Wired.OnCycle(); f.Wired.OnFastCycle();
        Assert.Equal(1, effect.Calls);
    }

    [Fact]
    public void NativeObjectEnqueueTracksSnapshotAndExactReplacementIdentity()
    {
        var f = new World(); var viewer = f.Human(); var item = f.Item(3); var wall = f.Wall(4);
        // Incremental objects never ready a viewer who has no full snapshot.
        f.Room.SendObject(item);
        Assert.Empty(f.Wired.CaptureFxViewers());
        f.Room.SendObjects(viewer.GetClient());
        var ready = Assert.Single(f.Wired.CaptureFxViewers());
        Assert.Contains(WiredVariableRuntimeFrames.FurniHolder(item), ready.ReadyHolders);
        Assert.Contains(WiredVariableRuntimeFrames.FurniHolder(wall), ready.ReadyHolders);
        Assert.DoesNotContain(wall, f.Context().Targets.AllFurni());
        f.Remove(item); var replacement = f.Item(3);
        Assert.DoesNotContain(WiredVariableRuntimeFrames.FurniHolder(replacement), Assert.Single(f.Wired.CaptureFxViewers()).ReadyHolders);
        f.Room.SendObject(replacement);
        Assert.Contains(WiredVariableRuntimeFrames.FurniHolder(replacement), Assert.Single(f.Wired.CaptureFxViewers()).ReadyHolders);
        f.Wired.Cleanup(); Assert.Empty(f.Wired.CaptureFxViewers());
    }

    [Fact]
    public void FailedFullRoomSnapshotLeavesViewerUnready()
    {
        var f = new World(); var viewer = f.Human();
        viewer.GetClient().SendCallback = _ => throw new IOException("snapshot enqueue failed");
        Assert.Throws<IOException>(() => f.Room.SendObjects(viewer.GetClient()));
        Assert.Empty(f.Wired.CaptureFxViewers());
    }

    [Fact]
    public void FailedNativeObjectEnqueueDoesNotPublishFxReadiness()
    {
        var f = new World(); var viewer = f.Human();
        f.Room.SendObjects(viewer.GetClient());
        var item = f.Item(3);
        viewer.GetClient().SendCallback = _ => throw new IOException("enqueue failed");
        f.Room.SendObject(item);
        Assert.DoesNotContain(WiredVariableRuntimeFrames.FurniHolder(item), Assert.Single(f.Wired.CaptureFxViewers()).ReadyHolders);
    }

    [Fact]
    public void NativeSpatialVariableWritesUsePlacementPolicyAndRejectCreatorOrReplacedTargets()
    {
        var f = new World(); f.PrepareVariables(); var template = f.Item(3); template.Definition.Stackable = true; f.Remove(template);
        var mover = Assert.IsType<Item>(f.Room.GetRoomItemHandler().PlaceTemporaryFloorItem(template.Definition, 1, 0, 0, 0));
        var blocker = f.Item(4); blocker.SetState(1, 0, 0, Gamemap.GetAffectedTiles(1, 1, 1, 0, 0)); f.Map.AddToMap(blocker);
        var context = f.Context(); var frame = WiredVariableRuntimeFrames.Create(context);
        var holder = WiredVariableRuntimeFrames.FurniHolder(mover);
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "internal:@position.x");
        Assert.False(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 1, frame));
        context.Policy.Addons.Physics = new(false, new HashSet<uint> { blocker.Id }, new HashSet<int>(), new HashSet<uint>());
        Assert.True(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 1, frame));
        Assert.Equal(1, mover.GetX);
        Assert.Contains(mover, f.Map.GetCoordinatedItems(new(1, 0)));
        Assert.False(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 2, new(1, [holder])));
        f.Remove(mover); var replacement = f.Item(mover.Id);
        Assert.False(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 2, frame));
        Assert.Equal(0, replacement.GetX);
    }

    [Fact]
    public void NativeProjectileBuiltinUsesActualRoomClockAndDetachLifecycle()
    {
        var f = new World(); f.PrepareVariables(); var item = f.Item(3);
        var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        var frame = new WiredVariableFrame(1, [holder]); // Menus and FX need the real clock without a firing context.
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "internal:@projectile.animation.position.x");
        Assert.Null(f.Wired.Variables.Module.Read(reference, holder, frame));
        item.SetState(2, 0, 0, Gamemap.GetAffectedTiles(1, 1, 2, 0, 0));
        Assert.True(WiredProjectileFlights.For(f.Room).Begin(item, 0, 0, 0, 0, 0));
        Assert.Equal(2, f.Wired.Variables.Module.Read(reference, holder, frame)!.Value);
        f.Wired.DetachRoomItem(item); Assert.Null(f.Wired.Variables.Module.Read(reference, holder, frame));
        Assert.True(WiredProjectileFlights.For(f.Room).Begin(item, 0, 0, 0, 0, 0));
        f.Wired.Cleanup(); Assert.Null(f.Wired.Variables.Module.Read(reference, holder, frame));
    }

    [Fact]
    public void NativeBuiltinStateCompletionPublishesOnceForCreatorAndFiringFrames()
    {
        var f = new World(); f.PrepareVariables(); var item = f.Item(3);
        var triggerItem = f.Item(1); triggerItem.Definition.InteractionName = "wf_trg_state_changed";
        var trigger = f.Wired.CreateConfiguredBox(triggerItem)!;
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [0, 100], SelectedItems = [item.Id] }, out var config, out var error), error);
        trigger.ApplyConfiguration(config); Assert.True(f.Wired.AddBox(trigger));
        var effect = f.Effect(); var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "internal:@state");
        Assert.True(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 1, new(1, [holder])));
        f.Wired.OnFastCycle(); Assert.Equal(1, effect.Calls);
        Assert.False(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 1, new(1, [holder])));
        f.Wired.OnCycle(); Assert.Equal(1, effect.Calls);
        Assert.True(f.Wired.Variables.Module.Mutate(reference, holder, WiredVariableMutation.Set, 0, WiredVariableRuntimeFrames.Create(f.Context())));
        f.Wired.OnFastCycle(); Assert.Equal(2, effect.Calls);
        f.Remove(item); var replacement = f.Item(item.Id);
        f.Wired.PublishBuiltinStateChanged(item, new(1, [holder]));
        f.Wired.OnCycle(); Assert.Equal(2, effect.Calls);
        Assert.Equal("0", replacement.LegacyDataString);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 7)]
    [InlineData(3, 0)]
    public void NativeJoinCapturesEntryFactsForTheActualVisit(int method, int teleporterId)
    {
        var f = new World(); var prior = f.Human(); var client = prior.GetClient(); var player = client.GetHabbo();
        var destination = f.Item(7);
        if (method == 2) { player.IsTeleporting = true; player.TeleporterId = destination.Id; }
        if (method == 3) f.Wired.RecordRoomNetworkForward(prior, f.Room.Id);
        f.DetachUser(prior);
        var gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousGame = gameField.GetValue(null);
        try
        {
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(game, "_clientManager", WiredEditorPromotionTests.Proxy.Create<IGameClientManager>((method, _) =>
                method.Name == "GetClientByUserId" ? client : null));
            gameField.SetValue(null, game);
            Assert.True(f.Room.GetRoomUserManager().AddAvatarToRoom(client));
            var joined = f.Room.GetRoomUserManager().GetRoomUserByHabbo(player.Id);
            Assert.NotNull(joined); Assert.NotSame(prior, joined);
            player.IsTeleporting = false; player.IsHopping = false; player.TeleporterId = 0;
            var frame = WiredVariableRuntimeFrames.Create(f.Context()); var holder = WiredVariableRuntimeFrames.UserHolder(joined);
            Assert.Equal(method, f.Wired.ReadBuiltin(new(WiredVariableTarget.User, "internal:@room_entry.method"), holder, frame));
            Assert.Equal(teleporterId, f.Wired.ReadBuiltin(new(WiredVariableTarget.User, "internal:@room_entry.teleport_id"), holder, frame));
            Assert.Equal(0u, player.WiredRoomNetworkDestination);
        }
        finally { gameField.SetValue(null, previousGame); }
    }

    [Fact]
    public void ForwardTokenIsDestinationScopedAndTeleportTakesPrecedence()
    {
        var f = new World(); var actor = f.Human(); var player = actor.GetClient().GetHabbo();
        f.Wired.RecordRoomNetworkForward(actor, f.Room.Id + 1);
        Assert.Equal(default, WiredRoomEntrySnapshot.Capture(f.Room, player));
        Assert.Equal(0u, player.WiredRoomNetworkDestination);
        player.IsTeleporting = true; player.TeleporterId = f.Item(7).Id;
        f.Wired.RecordRoomNetworkForward(actor, f.Room.Id);
        Assert.Equal(new(WiredRoomEntryMethod.Teleport, 7), WiredRoomEntrySnapshot.Capture(f.Room, player));
        f.DetachUser(actor); f.Wired.RecordRoomNetworkForward(actor, f.Room.Id);
        Assert.Equal(0u, player.WiredRoomNetworkDestination);
    }

    [Fact]
    public void NativeWallProviderSeparatesWallCoordinatesOffsetAndAltitudeAndUsesUpdatePath()
    {
        var f = new World(); var viewer = f.Human(); var wall = f.Wall(4);
        var context = f.Context(new(WiredEventKind.ClickFurni) { EventItem = wall });
        context.Triggering.FurniIds.Add(wall.Id);
        var frame = WiredVariableRuntimeFrames.Create(context); var holder = WiredVariableRuntimeFrames.FurniHolder(wall);
        int? Read(string key) => f.Wired.ReadBuiltin(new(WiredVariableTarget.Furni, "internal:" + key), holder, frame);
        bool Write(string key, int value) => f.Wired.WriteBuiltin(new(WiredVariableTarget.Furni, "internal:" + key), holder, value, frame);
        Assert.Equal(1, Read("@position.x")); Assert.Equal(1, Read("@position.y"));
        Assert.Equal(10, Read("@wallitem_offset")); Assert.Equal(2000, Read("@altitude")); Assert.Equal(4, Read("@rotation"));
        var packets = 0; ((FlashGameClient)viewer.GetClient()).SendCallback = _ => { packets++; return true; };
        Assert.True(Write("@wallitem_offset", 12)); Assert.True(Write("@altitude", 2100)); Assert.True(Write("@rotation", 6));
        Assert.Equal(":w=1,1 l=12,21 r", wall.WallCoordinates);
        Assert.Equal(3, packets);
        Assert.Same(wall, ((ConcurrentDictionary<uint, Item>)Get(f.Room.GetRoomItemHandler(), "_movedItems"))[wall.Id]);
        Assert.False(Write("@altitude", 2150)); Assert.False(Write("@position.x", 701)); Assert.False(Write("@rotation", 2));
        Assert.Equal(":w=1,1 l=12,21 r", wall.WallCoordinates);
        var replacement = f.Wall(wall.Id);
        Assert.False(Write("@wallitem_offset", 13)); Assert.Null(Read("@wallitem_offset")); Assert.Equal(":w=1,1 l=10,20 l", replacement.WallCoordinates);
        replacement.WallCoordinates = "bad wall bytes";
        Assert.Null(Read("@wallitem_offset"));
    }

    [Fact]
    public void NativeTimezoneOverrideBindsCalendarAndVariablesWhileDefaultsRemainDistinct()
    {
        var f = new World(); var store = new SettingsStore();
        Set(f.Wired, "_settings", new WiredRoomSettings(f.Room, store));
        Assert.Equal(DateTimeOffset.Now.Offset, f.Wired.CalendarTime.Offset);
        Assert.Equal(TimeZoneInfo.Utc, f.Wired.Variables.TimeZone());
        f.Human(); f.Room.Type = "private"; f.Room.OwnerName = "viewer";
        var owner = f.Room.GetRoomUserManager().GetRoomUserByHabbo(42).GetClient();
        Assert.True(f.Wired.Settings.TrySave(owner, 0, 0, "Pacific/Honolulu", out var error), error);
        Assert.Equal(TimeSpan.FromHours(-10), f.Wired.CalendarTime.Offset);
        Assert.Equal("Pacific/Honolulu", f.Wired.Variables.TimeZone().Id);
        var item = f.Item(1); item.Definition.InteractionName = "wf_cnd_match_time";
        var box = f.Wired.CreateConfiguredBox(item)!;
        var hour = f.Wired.CalendarTime.Hour;
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [1, hour, hour, 0, 0, 0, 0, 0, 0] }, out var config, out error), error);
        box.ApplyConfiguration(config);
        Assert.True(((IWiredContextualItem)box).Execute(f.Context()));
        Assert.True(f.Wired.Settings.TrySave(owner, 0, 0, "", out error), error);
        Assert.Equal(DateTimeOffset.Now.Offset, f.Wired.CalendarTime.Offset);
        Assert.Equal(TimeZoneInfo.Utc, f.Wired.Variables.TimeZone());
    }

    private sealed class SettingsStore : IWiredRoomSettingsStore
    {
        private WiredRoomSettingsSnapshot? _saved;
        public WiredRoomSettingsSnapshot? Load(uint roomId) => _saved;
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings) => _saved = settings;
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
        public void PrepareVariables()
        {
            // Keep native variable read/write behavior; isolate only storage authority for these non-SQL regressions.
            var variables = Wired.Variables;
            var module = new WiredVariableModule(Room.Id, new OwnerDirectory(), new MemoryWiredVariableStore(), () => 0,
                new RoomWiredBuiltinVariables(Room, engineRead: Wired.ReadBuiltin, engineWrite: Wired.WriteBuiltin, stateChanged: Wired.PublishBuiltinStateChanged));
            typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(variables, module);
        }
        public void Remove(Item item) => _items.TryRemove(item.Id, out _);
        public void DetachUser(RoomUser user) => _users.TryRemove(user.InternalRoomId, out _);
        public WiredRuntimeContext Context(WiredRuntimeEvent? evt = null) => new(Room, evt ?? new(WiredEventKind.Periodic),
            new(() => _items.Values, () => _users.Values, id => Room.GetRoomItemHandler().GetItem(id),
                id => Room.GetRoomUserManager().GetRoomUserByVirtualId(id)), Wired);
        public CounterAction Effect()
        {
            var item = Item(2); item.Definition.InteractionType = InteractionType.WiredEffect;
            var effect = new CounterAction(Room, item); Assert.True(Wired.AddBox(effect)); return effect;
        }
        public RoomUser Human()
        {
            var client = new FlashGameClient(null!, new FlashPacketFactory())
            {
                Revision = new() { InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.FieldType == typeof(uint)).Select(field => (uint)field.GetValue(null)!).Distinct().ToDictionary(id => id, id => id) },
                SendCallback = _ => true
            };
            var habbo = new Habbo { Id = 42, Username = "viewer", Motto = "", Look = "test", Gender = "M", CurrentRoom = Room, Client = client,
                HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0), Effects = new EffectsComponent(), Permissions = new([], []) };
            client.SetHabbo(habbo);
            var user = new RoomUser(42, 1, 1, Room); Set(user, "_mClient", client); _users[user.VirtualId] = user; return user;
        }
        public RoomUser Bot()
        {
            var user = new RoomUser(0, 1, 7, Room) { BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot)), InternalRoomId = 7 };
            _users[user.VirtualId] = user; return user;
        }
        public Item Wall(uint id)
        {
            var item = Item(id); Remove(item); item.Definition.Type = ItemType.Wall; item.WallCoordinates = ":w=1,1 l=10,20 l";
            ((ConcurrentDictionary<uint, Item>)Get(Room.GetRoomItemHandler(), "_wallItems"))[id] = item;
            return item;
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
    private sealed class OwnerDirectory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => 1;
        public WiredVariableDefinition? Find(uint itemId) => null;
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
