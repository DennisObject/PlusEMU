using System.Collections.Concurrent;
using System.Reflection;
using Dapper;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.AreaHide;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public sealed class AreaHideParityTests
{
    [Fact]
    public void CanonicalNumberDataAndOctaneAdapterMatchExistingClientShapes()
    {
        var item = AreaItem();
        Assert.True(AreaHideState.TryRead(item, out var state));
        Assert.Equal(new int[8], state.Values);
        Assert.IsType<IntArrayDataFormat>(item.Definition.CreateData());
        Assert.Equal(InteractionType.AreaHide, InteractionTypes.GetTypeFromString("area_hide"));
        item.ExtraData = AreaHideState.Load("1\n2\n3\n4\n5\n1\n0\n1");
        Assert.True(AreaHideState.TryRead(item, out state));
        var packet = new RecordingPacket();
        var composer = new AreaHideComposer(item.Id, state);
        composer.Compose(packet);
        Assert.Equal(6001u, composer.MessageId);
        Assert.Equal(new object[] { 300u, true, 2, 3, 4, 5, true, false, true }, packet.Writes);
        var removed = new RecordingPacket();
        new AreaHideComposer(item.Id, state, removed: true).Compose(removed);
        Assert.Equal(false, removed.Writes[1]);
        Assert.Throws<FormatException>(() => AreaHideState.Load("1\n2\n3\n4\n5\n6\n7\n8\n9"));
    }

    [WiredChestDatabaseFact]
    public void MigrationRepairsOnlyMatchingAssetFloorSpriteAndKnownWrongInteractionAndRepeatsSafely()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("ALTER TABLE furniture ADD COLUMN type CHAR(1), ADD COLUMN sprite_id INT; INSERT INTO furniture VALUES(1,'conf_area_hide','default','s',15215),(2,'conf_area_hide','default','i',15215),(3,'conf_area_hide','default','s',999),(4,'conf_area_hide','custom_logic','s',15215),(5,'conf_area_hide_other','default','s',15215)");
        var sql = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/67_AreaHideInteraction.sql"));
        fixture.Connection.Execute(sql);
        fixture.Connection.Execute(sql);
        Assert.Equal("area_hide", fixture.Connection.QuerySingle<string>("SELECT interaction_type FROM furniture WHERE id=1"));
        Assert.Equal(new[] { "default", "default", "custom_logic", "default" }, fixture.Connection.Query<string>("SELECT interaction_type FROM furniture WHERE id>1 ORDER BY id"));
    }

    [WiredChestDatabaseFact]
    public async Task ActualMapSavePersistsReloadsAndRejectsUnauthorizedOrInvalidFields()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var (world, item, metadata) = World(fixture);
        var handler = new SaveBrandingItemEvent(metadata);
        var fields = Fields();
        await handler.Parse(world.Client, Incoming([300, fields.Length, .. fields.Cast<object>()]));
        Assert.True(AreaHideState.TryRead(item, out var saved));
        Assert.Equal(new[] { 1, 1, 2, 3, 4, 1, 0, 1 }, saved.Values);
        var stored = fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300");
        Assert.Equal(saved.Values, AreaHideState.Load(stored).Data);
        Assert.Contains(world.Packets, packet => packet.Header == 6001);
        var packetCount = world.Packets.Count;
        var guestHabbo = new Habbo { Id = 2, Username = "guest", CurrentRoom = world.Room, Access = EditorTestSupport.Access([]) };
        var (guest, _) = Client(guestHabbo);
        await handler.Parse(guest, Incoming([300, fields.Length, .. fields.Cast<object>()]));
        Assert.Equal(stored, fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300"));
        var malformed = new[] { Fields(width: 0), Fields(width: 21), Fields(rootX: 4), Fields(flag: 2), Fields(state: -1), Fields().Concat(new[] { "unknown", "0" }).ToArray() };

        foreach (var values in malformed) {
            await handler.Parse(world.Client, Incoming([300, values.Length, .. values.Cast<object>()]));
        }

        var duplicate = Fields();
        duplicate[2] = "state";
        await handler.Parse(world.Client, Incoming([300, duplicate.Length, .. duplicate.Cast<object>()]));
        Assert.Equal(packetCount, world.Packets.Count);
        Assert.Equal(stored, fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300"));
        // Controller rights can configure, without the unrelated branding/staff permission.
        world.Room.UsersWithRights.Add(2);
        await handler.Parse(guest, Incoming([300, fields.Length, .. Fields(state: 0).Cast<object>()]));
        Assert.True(AreaHideState.TryRead(item, out saved));
        Assert.False(saved.On);
    }

    [WiredChestDatabaseFact]
    public void StorageFailurePrecedesMutationAndIndexedSmartWritesKeepSourceLimits()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var (world, item, _) = World(fixture);
        fixture.Connection.Execute("CREATE TRIGGER reject_area_save BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='area failure'");
        Assert.Throws<MySqlConnector.MySqlException>(() => AreaHideState.Set(world.Room, item, 1, 3));
        Assert.True(AreaHideState.TryRead(item, out var unchanged));
        Assert.Equal(0, unchanged[1]);
        Assert.Empty(world.Packets);
        fixture.Connection.Execute("DROP TRIGGER reject_area_save");
        Assert.True(AreaHideState.Set(world.Room, item, 3, int.MaxValue)); // Smart size range differs from controller max20.
        Assert.False(AreaHideState.Set(world.Room, item, 3, -1));
        Assert.False(AreaHideState.Set(world.Room, item, 5, 2));
        Assert.True(AreaHideState.Set(world.Room, item, 5, 1));
        Assert.False(AreaHideState.Set(world.Room, item, 8, 1));
        fixture.Connection.Execute("UPDATE items SET room_id=0 WHERE id=300");
        Assert.Throws<InvalidOperationException>(() => AreaHideState.Set(world.Room, item, 1, 2));
        Assert.True(AreaHideState.TryRead(item, out unchanged));
        Assert.Equal(0, unchanged[1]);
    }

    [WiredChestDatabaseFact]
    public void ToggleJoinUpdateAndRemovalResetUseActualControllerState()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var (world, item, _) = World(fixture);
        Assert.True(AreaHideState.Configure(world.Room, world.Client, item, Fields()));
        world.Packets.Clear();
        item.Interactor.OnTrigger(world.Client, item, 0, true);
        Assert.True(AreaHideState.TryRead(item, out var state));
        Assert.False(state.On);
        Assert.Equal("0", fixture.Connection.QuerySingle<string>("SELECT SUBSTRING_INDEX(extra_data,CHAR(10),1) FROM items WHERE id=300"));
        item.Interactor.OnWiredTrigger(item);
        Assert.True(AreaHideState.TryRead(item, out state));
        Assert.True(state.On);
        world.Packets.Clear();
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.Room.GetRoomUserManager())!;
        users.Clear(); // Furniture snapshot is independent of avatar snapshot services.
        world.Room.SendObjects(world.Client);
        Assert.Equal(1, Assert.Single(world.Packets.Where(packet => packet.Header == 6001)).Payload[4]);
        users.TryAdd(world.Actor.VirtualId, world.Actor);
        world.Packets.Clear();
        Assert.True(world.Room.GetRoomItemHandler().RemoveFurniture(world.Client, item));
        Assert.Equal(0, Assert.Single(world.Packets.Where(packet => packet.Header == 6001)).Payload[4]);
        Assert.False(AreaHideState.Set(world.Room, item, 1, 3));
        Assert.True(AreaHideState.TryRead(item, out state));
        Assert.True(state.On); // Pickup resets the client's area; the saved controller settings survive reentry.
        var loaded = AreaItem();
        loaded.ExtraData = AreaHideState.Load(fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300"));
        world.Packets.Clear();
        AreaHideState.SendSnapshot(world.Client, [loaded]);
        Assert.Equal(1, Assert.Single(world.Packets).Payload[4]);
    }

    [WiredChestDatabaseFact]
    public void ActualBuiltinModulePersistsNumericAndPresenceChangesWithoutInventingFlagValues()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var (world, item, _) = World(fixture);
        var context = new WiredRuntimeContext(world.Room, new(WiredEventKind.Use) { Actor = world.Actor, EventItem = item },
            new(() => [item], () => [world.Actor], world.Room.GetRoomItemHandler().GetItem), world.Room.GetWired());
        var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        var frame = WiredVariableRuntimeFrames.Create(context);
        var module = new WiredVariableModule(world.Room.Id, new AreaDirectory(), new MemoryWiredVariableStore(), world.Clock,
            new RoomWiredBuiltinVariables(world.Room));
        WiredVariableReference Reference(string key) => new(WiredVariableTarget.Furni, "internal:~area_hide." + key);
        bool Mutate(string key, WiredVariableMutation mutation, long value = 0) => module.Mutate(Reference(key), holder, mutation, value, frame);

        foreach (var key in new[] { "root_x", "root_y", "width", "length" }) {
            Assert.True(RoomWiredBuiltinVariables.HasNumericValue(Reference(key)));
            Assert.Equal(0, module.Read(Reference(key), holder, frame)!.Value);
        }

        Assert.True(Mutate("root_x", WiredVariableMutation.Set, 3));
        Assert.True(Mutate("width", WiredVariableMutation.Set, int.MaxValue));
        Assert.False(Mutate("root_x", WiredVariableMutation.Set, -1));
        var saved = AreaHideState.Load(fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300"));
        Assert.Equal(3, saved.Data[1]);
        Assert.Equal(int.MaxValue, saved.Data[3]);
        Assert.Equal(2, module.DrainChanges().Count);

        foreach (var key in new[] { "is_invisible_furni", "hiding_wallitems", "inverted" }) {
            Assert.False(RoomWiredBuiltinVariables.HasNumericValue(Reference(key)));
            Assert.Null(module.Read(Reference(key), holder, frame));
            Assert.False(Mutate(key, WiredVariableMutation.Set, 1));
            Assert.True(Mutate(key, WiredVariableMutation.Give));
            Assert.Equal(1, module.Read(Reference(key), holder, frame)!.Value);
            Assert.False(Mutate(key, WiredVariableMutation.Give));
            Assert.False(Mutate(key, WiredVariableMutation.Set, 0));
            Assert.True(Mutate(key, WiredVariableMutation.Remove));
            Assert.Null(module.Read(Reference(key), holder, frame));
            Assert.False(Mutate(key, WiredVariableMutation.Remove));
        }

        Assert.Empty(module.DrainChanges());
        Assert.False(module.Mutate(Reference("inverted"), holder with { StableId = 999 }, WiredVariableMutation.Give, 0, frame));
        Assert.False(module.Mutate(Reference("inverted"), holder, WiredVariableMutation.Give, 0, new(99, [holder])));
        world.Packets.Clear();
        fixture.Connection.Execute("CREATE TRIGGER reject_area_save BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='area failure'");
        Assert.Throws<MySqlConnector.MySqlException>(() => Mutate("inverted", WiredVariableMutation.Give));
        Assert.Null(module.Read(Reference("inverted"), holder, frame));
        Assert.Empty(world.Packets);
        Assert.Empty(module.DrainChanges());
        fixture.Connection.Execute("DROP TRIGGER reject_area_save");
        var floors = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.Room.GetRoomItemHandler())!;
        floors.TryRemove(item.Id, out _);
        Assert.False(Mutate("inverted", WiredVariableMutation.Give));
        Assert.False(Mutate("root_x", WiredVariableMutation.Set, 2));
        Assert.Null(module.Read(Reference("inverted"), holder, frame));
    }

    [WiredChestDatabaseFact]
    public void CapturedBuiltinFrameCannotMutateSameIdReplacementButFreshFrameCan()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var (world, original, _) = World(fixture);
        Assert.True(AreaHideState.Set(world.Room, original, 7, 1));
        var handling = world.Room.GetRoomItemHandler();
        WiredVariableFrame Frame(Item item) => WiredVariableRuntimeFrames.Create(new(world.Room,
            new(WiredEventKind.Use) { Actor = world.Actor, EventItem = item },
            new(() => [item], () => [world.Actor], handling.GetItem), world.Room.GetWired()));
        var captured = Frame(original);
        var holder = WiredVariableRuntimeFrames.FurniHolder(original);
        var module = new WiredVariableModule(world.Room.Id, new AreaDirectory(), new MemoryWiredVariableStore(), world.Clock,
            new RoomWiredBuiltinVariables(world.Room));
        WiredVariableReference Reference(string key) => new(WiredVariableTarget.Furni, "internal:~area_hide." + key);
        var saved = fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300");
        var replacement = AreaItem();
        replacement.ExtraData = AreaHideState.Load(saved);
        replacement.Attach(world.Room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        var floors = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handling)!;
        floors[original.Id] = replacement;
        world.Packets.Clear();
        var attempts = new[] {
            module.Mutate(Reference("root_x"), holder, WiredVariableMutation.Set, 2, captured),
            module.Mutate(Reference("is_invisible_furni"), holder, WiredVariableMutation.Give, 0, captured),
            module.Mutate(Reference("inverted"), holder, WiredVariableMutation.Remove, 0, captured)
        };
        Assert.Equal(new[] { false, false, false }, attempts);
        Assert.Equal(saved, fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300"));
        Assert.Equal(AreaHideState.Load(saved).Data, Assert.IsType<IntArrayDataFormat>(replacement.ExtraData).Data);
        Assert.Empty(world.Packets);
        Assert.Empty(module.DrainChanges());
        var fresh = Frame(replacement);
        Assert.True(module.Mutate(Reference("root_x"), holder, WiredVariableMutation.Set, 2, fresh));
        Assert.True(module.Mutate(Reference("is_invisible_furni"), holder, WiredVariableMutation.Give, 0, fresh));
        Assert.True(module.Mutate(Reference("inverted"), holder, WiredVariableMutation.Remove, 0, fresh));
        var reloaded = AreaHideState.Load(fixture.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=300"));
        Assert.Equal(2, reloaded.Data[1]);
        Assert.Equal(1, reloaded.Data[5]);
        Assert.Equal(0, reloaded.Data[7]);
        Assert.Single(module.DrainChanges()); // Only the numeric field permits interception.
    }

    private sealed class AreaDirectory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => null;
        public uint? GetRoomOwner(uint roomId) => roomId == 42 ? 1u : null;
    }

    private static string[] Fields(int width = 3, int rootX = 1, int flag = 1, int state = 1) =>
        ["state", state.ToString(), "rootX", rootX.ToString(), "rootY", "2", "width", width.ToString(), "length", "4", "invisibility", flag.ToString(), "wallItems", "0", "invert", "1"];
    private static Item AreaItem() => new()
    {
        Id = 300,
        OwnerId = 1,
        RoomId = 42,
        ExtraData = AreaHideState.Load("0"),
        Definition = new() { Id = 1, ItemName = "conf_area_hide", InteractionType = InteractionType.AreaHide, Type = ItemType.Floor, Width = 1, Length = 1 }
    };
    private static (WiredChestProtocolTests.World World, Item Item, RoomItemMetadataService Metadata) World(WiredChestDatabaseTests.Fixture fixture)
    {
        var world = new WiredChestProtocolTests.World(fixture.Database);
        world.Room.Type = "private";
        world.Room.OwnerName = "owner";
        world.Room.UsersWithRights = [];
        world.Habbo.Username = "owner";
        world.Habbo.Access = EditorTestSupport.Access([]);
        Set(world.Room, "_interactionClock", world.Clock);
        var store = new RoomItemMetadataStore(fixture.Database);
        Set(world.Room, "_roomItemHandling", new RoomItemHandling(world.Room, TestRoomItemStore.Instance, store, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        Set(world.Room, "_gamemap", new Gamemap(world.Room, new RoomModel("test", 0, 0, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        world.Room.GetGameMap().GenerateMaps();
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(300,1,42,1,'0')");
        var item = AreaItem();
        item.Attach(world.Room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        var handling = world.Room.GetRoomItemHandler();
        ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handling)!).TryAdd(item.Id, item);

        return (world, item, new(store, null!));
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
