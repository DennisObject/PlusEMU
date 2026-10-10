using System.Reflection;
using System.Text.Json;
using Dapper;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(":w=1,2 l=11,53 l", ":w=2,1 l=20,80 l")]
    [InlineData(":w=1,2 l=11,53 r", ":w=2,1 l=20,80 r")]
    [InlineData(":w=0,6 l=11,1022 l a=200", ":w=0,7 l=20,1080 l a=720")]
    [InlineData(":w=4,0 l=20,1031 r a=200", ":w=5,0 l=11,1100 r a=720")]
    public void WallSnapshotSaveCapturesAndRestoresRawPlacement(string baseline, string moved)
    {
        var store = WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, baseline);
        var floor = Furni(302, InteractionType.None, WiredBoxType.None);
        floor.RoomId = RoomId;
        _room.GetRoomItemHandler().LoadFurniture([wall, floor]);
        Assert.Equal(baseline, wall.WallCoordinates);
        var action = WallSnapshotAction();
        Assert.True(WiredNativeTestSupport.TrySavePrepared(action, new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] },
            TestWiredConfigurationStore.Instance, out var error), error);
        var snapshot = Assert.Single(action.Configuration.Snapshots);
        Assert.Equal(wall.Id, snapshot.ItemId);
        Assert.Contains("\"Wall\":", JsonSerializer.Serialize(snapshot));
        var originalFloor = WiredRoomOperations.Capture(floor);
        wall.WallCoordinates = moved;
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
        _client.Packets.Clear();
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(baseline, wall.WallCoordinates);
        Assert.Equal((wall.Id, baseline), Assert.Single(store.Writes));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.ItemUpdateComposer);
        Assert.Equal(originalFloor, WiredRoomOperations.Capture(floor));
        Assert.False(action.Execute(WallSnapshotContext()));
        Assert.Single(store.Writes);
    }

    [Fact]
    public void WallSnapshotIdentityIncludesWallsWithoutExpandingRoomFloorSources()
    {
        WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, ":w=1,2 l=11,53 l");
        var floor = Furni(302, InteractionType.None, WiredBoxType.None);
        floor.RoomId = RoomId;
        _room.GetRoomItemHandler().LoadFurniture([wall, floor]);
        var context = WallSnapshotContext();
        Assert.Same(wall, Assert.Single(context.Targets.ResolveFurni(context, [wall.Id], WiredSources.Selected)));
        Assert.Same(floor, Assert.Single(context.Targets.ResolveFurni(context, [], WiredSources.AllRoom)));
        var replacement = WallSnapshotItem(wall.Id, ":w=2,1 l=20,80 l");
        _room.GetRoomItemHandler().LoadFurniture([replacement, floor]);
        Assert.Empty(context.Targets.ResolveFurni(context, [wall.Id], WiredSources.Selected));
        var fresh = WallSnapshotContext();
        Assert.Same(replacement, Assert.Single(fresh.Targets.ResolveFurni(fresh, [wall.Id], WiredSources.Selected)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(0, 1, 1, 1)]
    public void WallSnapshotUnsupportedFlagsDoNotFallThroughToFloorMovement(int state, int rotation, int position, int altitude)
    {
        var store = WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, ":w=1,2 l=11,53 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallSnapshotAction();
        Assert.True(WiredNativeTestSupport.TrySavePrepared(action, new() { IntParams = [state, rotation, position, altitude, 100], SelectedItems = [wall.Id] },
            TestWiredConfigurationStore.Instance, out var error), error);
        wall.WallCoordinates = ":w=2,1 l=20,80 r";
        Assert.False(action.Execute(WallSnapshotContext()));
        Assert.Equal(":w=2,1 l=20,80 r", wall.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.False(new WiredRoomMovement((_, _, _) => { }).MoveFurniture(WallSnapshotContext(), wall, 1, 1, 0, 0));
    }

    [Fact]
    public void WallSnapshotPersistenceFailureAndSameIdReplacementLeaveStateAndPacketsUntouched()
    {
        var store = WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, ":w=1,2 l=11,53 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallSnapshotAction();
        Assert.True(WiredNativeTestSupport.TrySavePrepared(action, new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] },
            TestWiredConfigurationStore.Instance, out var error), error);
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
        wall.WallCoordinates = ":w=2,1 l=20,80 l";
        var context = WallSnapshotContext();
        _client.Packets.Clear();
        store.Fail = true;
        Assert.Throws<InvalidOperationException>(() => action.Execute(context));
        Assert.Equal(":w=2,1 l=20,80 l", wall.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.Empty(_client.Packets);
        store.Fail = false;
        var replacement = WallSnapshotItem(wall.Id, ":w=2,1 l=20,80 l");
        _room.GetRoomItemHandler().LoadFurniture([replacement]);
        Assert.False(action.Execute(context));
        Assert.Equal(":w=2,1 l=20,80 l", replacement.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.Empty(_client.Packets);
        var fresh = WallSnapshotContext();
        Assert.True(action.Execute(fresh));
        Assert.Equal(":w=1,2 l=11,53 l", replacement.WallCoordinates);
        Assert.Single(store.Writes);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.ItemUpdateComposer);
    }

    [Theory]
    [InlineData(":w=1,2 l=11,53 l a=200 unknown")]
    [InlineData(":w=1,2 l=11,53 l a=2147483648")]
    [InlineData(":w=1,2 l=11,53 l a=not-an-int")]
    [InlineData(":w=1,2 l=11,53 l a=-2147483649")]
    [InlineData(":w=1,2 l=11,53 x a=200")]
    public void WallSnapshotMalformedNativePlacementsAreRejected(string location)
    {
        Assert.Null(_room.GetRoomItemHandler().WallPositionCheck(location));
        Assert.False(WiredWallSnapshot.TryParse(location, out _));
    }

    [Fact]
    public void WallSnapshotOptionalJsonPreservesLegacyFloorShapeAndRejectsInvalidWallPayload()
    {
        const string old = "{\"ItemId\":1,\"DefinitionId\":2,\"X\":3,\"Y\":4,\"Z\":5,\"Rotation\":6,\"State\":\"0\"}";
        var floor = JsonSerializer.Deserialize<WiredFurniSnapshot>(old)!;
        Assert.Null(floor.Wall);
        Assert.Equal(old, JsonSerializer.Serialize(floor));
        var wall = floor with { Wall = new(0, 6, 11, 1022, true, 200) };
        var restored = JsonSerializer.Deserialize<WiredFurniSnapshot>(JsonSerializer.Serialize(wall));
        Assert.Equal(wall, restored);
        var bad = new WiredConfiguration { Snapshots = [wall with { Wall = wall.Wall with { TileX = 701 } }] };
        Assert.False(WiredLegacyProtocol.IsWithinLimits(bad));
    }

    [Fact]
    public void WallSnapshotPreservesAuthoritativeAltitudeWhenPixelsAreIdentical()
    {
        var store = WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=200");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallSnapshotAction();
        Assert.True(WiredNativeTestSupport.TrySavePrepared(action, new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] },
            TestWiredConfigurationStore.Instance, out var error), error);
        wall.WallCoordinates = ":w=3,7 l=12,61 l a=201";
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(":w=3,7 l=12,61 l a=200", wall.WallCoordinates);
        Assert.Equal(200, Assert.Single(action.Configuration.Snapshots).Wall!.NativeAltitude);
        Assert.Single(store.Writes);
    }

    [WiredChestDatabaseFact]
    public void WallSnapshotDatabaseRestoreReloadsAndRollsBackBeforeMemoryOrPublication()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("ALTER TABLE items ADD COLUMN wall_pos TEXT; ALTER TABLE wired_item_configurations ADD COLUMN schema_version INT NOT NULL DEFAULT 1");
        const string baseline = ":w=0,6 l=11,1022 l a=200";
        const string moved = ":w=0,7 l=20,1080 l a=720";
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data,wall_pos) VALUES(301,7,42,0,'',@baseline)", new { baseline });
        Set("_roomItemHandling", new RoomItemHandling(_room, new RoomItemStore(fixture.Database), TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        var wall = WallSnapshotItem(301, baseline);
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallSnapshotAction();
        var configurations = new WiredConfigurationStore(fixture.Database);
        Assert.True(WiredNativeTestSupport.TrySavePrepared(action, new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] },
            configurations, out var error), error);
        var stored = configurations.Load(action.Item.Id, action.Descriptor)!;
        Assert.Equal(Assert.Single(action.Configuration.Snapshots), Assert.Single(stored.Snapshots));
        var reloadedAction = WallSnapshotAction();
        reloadedAction.ApplyConfiguration(stored);
        wall.WallCoordinates = moved;
        fixture.Connection.Execute("UPDATE items SET wall_pos=@moved WHERE id=301", new { moved });
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
        _client.Packets.Clear();
        fixture.Connection.Execute("CREATE TRIGGER reject_wall_snapshot BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reject wall movement'");
        Assert.Throws<MySqlConnector.MySqlException>(() => reloadedAction.Execute(WallSnapshotContext()));
        Assert.Equal(moved, wall.WallCoordinates);
        Assert.Equal(moved, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Empty(_client.Packets);
        fixture.Connection.Execute("DROP TRIGGER reject_wall_snapshot");
        Assert.True(reloadedAction.Execute(WallSnapshotContext()));
        Assert.Equal(baseline, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.ItemUpdateComposer);
        new RoomItemStore(fixture.Database).MoveWall(301, RoomId, baseline); // Same-value persistence must not fail under changed-row connector settings.
        var reloadedWall = WallSnapshotItem(301, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        _room.GetRoomItemHandler().LoadFurniture([reloadedWall]);
        Assert.Equal(baseline, reloadedWall.WallCoordinates);
        reloadedWall.WallCoordinates = moved;
        fixture.Connection.Execute("UPDATE items SET room_id=99,wall_pos=@moved WHERE id=301", new { moved });
        _client.Packets.Clear();
        Assert.Throws<InvalidOperationException>(() => reloadedAction.Execute(WallSnapshotContext()));
        Assert.Equal(moved, reloadedWall.WallCoordinates);
        Assert.Equal(moved, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Empty(_client.Packets);
    }

    private WiredModernAction WallSnapshotAction()
    {
        Assert.True(WiredBoxRegistry.TryGet("wf_act_match_to_sshot", out var descriptor));

        var item = Furni(400, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition);
        item.RoomId = RoomId;
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, _room);

        return Assert.IsType<WiredModernAction>(_room.GetWired().CreateConfiguredBox(item, descriptor));
    }

    private WiredRuntimeContext WallSnapshotContext()
    {
        var wired = _room.GetWired();
        var targets = (WiredTargetResolver)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;

        var context = new WiredRuntimeContext(_room, new(WiredEventKind.GameStart), targets, wired);
        context.Policy.Addons.DisableAnimation = true;

        return context;
    }

    private Item WallSnapshotItem(uint id, string location)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None, ItemType.Wall);
        item.RoomId = RoomId;
        item.WallCoordinates = location;

        return item;
    }

    private WallSnapshotStore WallSnapshotInstallStore()
    {
        var store = new WallSnapshotStore();
        Set("_roomItemHandling", new RoomItemHandling(_room, store, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty,
            TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));

        return store;
    }

    private sealed class WallSnapshotStore : IRoomItemStore
    {
        public List<(uint Id, string Location)> Writes { get; } = [];
        public bool Fail { get; set; }
        public void AssignOwner(uint itemId, int userId) { }
        public void ClearRoom(uint itemId) { }
        public void SaveWallPosition(uint itemId, string wallPosition)
        {
            if (Fail) {
                throw new InvalidOperationException("Rejected wall persistence.");
            }

            Writes.Add((itemId, wallPosition));
        }
        public void MoveWall(uint itemId, uint roomId, string wallPosition) => SaveWallPosition(itemId, wallPosition);
        public void SaveMoved(IReadOnlyList<RoomItemSave> items) { }
        public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) { }
        public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) { }
    }
}
