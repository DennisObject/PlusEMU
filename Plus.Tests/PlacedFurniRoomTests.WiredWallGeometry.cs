using System.Buffers.Binary;
using System.Text.Json;
using Dapper;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("l", 12, "2", 61)]
    [InlineData("r", 12, "2", 57)]
    [InlineData("l", 12, "2.01", 61)]
    [InlineData("r", 12, "2.01", 57)]
    [InlineData("l", 12, "2.03", 60)]
    [InlineData("r", 12, "2.03", 56)]
    [InlineData("l", 12, "0", 125)]
    [InlineData("r", 12, "1", 89)]
    [InlineData("l", 11, "3.85", 3)]
    [InlineData("r", 11, "3.85", -2)]
    [InlineData("l", 11, "4.1", -5)]
    [InlineData("r", 11, "4.1", -10)]
    [InlineData("l", 12, "80", -2435)]
    public void WallAltitudeUsesNativeGeometryAndPositiveInfinityRounding(string side, int localX, string altitude, int pixelY)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var x = side == "l" ? 3 : 8;
        var y = side == "l" ? 7 : 0;
        var wall = WallSnapshotItem(301, $":w={x},{y} l={localX},16 {side}");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [2, 100], Text = altitude, SelectedItems = [wall.Id] });
        var context = WallSnapshotContext();
        context.Policy.Addons.DisableAnimation = true;
        Assert.True(action.Execute(context));
        var centiAltitude = decimal.ToInt32(decimal.Parse(altitude, System.Globalization.CultureInfo.InvariantCulture) * 100);
        Assert.Equal($":w={x},{y} l={localX},{pixelY} {side} a={centiAltitude}", wall.WallCoordinates);
        Assert.Single(store.Writes);
    }

    [Theory]
    [InlineData(true, "l", 28)]
    [InlineData(true, "r", 31)]
    [InlineData(false, "l", 62)]
    [InlineData(false, "r", 57)]
    public void WallAltitudeHonorsRoomScaleInsteadOfGlobalSixtyFour(bool halfScale, string side, int pixelY)
    {
        WallSnapshotInstallStore();
        WallGeometryModel(halfScale: halfScale);
        var wall = WallSnapshotItem(301, $":w=3,7 l=11,16 {side}");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [2, 100], Text = "2", SelectedItems = [wall.Id] });
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal($":w=3,7 l=11,{pixelY} {side} a=200", wall.WallCoordinates);
    }

    [Theory]
    [InlineData("l", 341, ":w=3,7 l=12,16 l", ":w=3,8 l=20,57 l a=200", ":w=3,7 l=12,61 l a=200", ":w=3,8 l=20,12 l a=341")]
    [InlineData("r", 329, ":w=8,0 l=12,16 r", ":w=9,0 l=20,61 r a=200", ":w=8,0 l=12,57 r a=200", ":w=9,0 l=20,20 r a=329")]
    public void WallPartialSnapshotRestoresIndependentSavedAltitudeAndPosition(string side, int altitude,
        string baseline, string moved, string positionOnly, string altitudeOnly)
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, baseline);
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_match_to_sshot", new() { IntParams = [0, 0, 1, 0, 100], SelectedItems = [wall.Id] });
        var snapshot = Assert.Single(action.Configuration.Snapshots);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        Assert.Equal(altitude, json.RootElement.GetProperty("Wall").GetProperty("CapturedAltitudeHundredths").GetInt32());
        Assert.Null(snapshot.Wall!.NativeAltitude);
        wall.WallCoordinates = moved;
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(positionOnly, wall.WallCoordinates);
        action.ApplyConfiguration(action.Configuration with { IntParams = [0, 0, 0, 1, 100] });
        wall.WallCoordinates = moved;
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(altitudeOnly, wall.WallCoordinates);
        // Subsequent floorplan changes must not reinterpret the stored altitude.
        WallGeometryModel(wallHeight: 15);
        wall.WallCoordinates = moved;
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.EndsWith($"{side} a={altitude}", wall.WallCoordinates);
    }

    [Fact]
    public void WallAltitudeReadsAuthoritativeAppendixAndFixedWallTop()
    {
        WallSnapshotInstallStore();
        WallGeometryModel(wallHeight: 15);
        var wall = WallSnapshotItem(301, ":w=0,6 l=11,1022 l a=200");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [0, 100], Text = "0.01", SelectedItems = [wall.Id] });
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(":w=0,6 l=11,1021 l a=201", wall.WallCoordinates);
    }

    [Fact]
    public void WallAltitudeExcludesRaisedEntranceFromDefaultWallTop()
    {
        WallSnapshotInstallStore();
        WallGeometryModel(wallHeight: -1, doorHeight: 7);
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,16 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [2, 100], Text = "2", SelectedItems = [wall.Id] });
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(":w=3,7 l=12,61 l a=200", wall.WallCoordinates);
        Assert.Equal(10.6, WiredWallGeometry.TileHeight(_room.GetGameMap().StaticModel, 1, 1));
    }

    [Theory]
    [InlineData(-1, 'k', 23.6)]
    [InlineData(-1, 'u', 29.6)]
    [InlineData(30, '0', 59.6)]
    public void WallDefaultAndFixedTopHonorAdditionalHeightCap(int fixedHeight, char interiorHeight, double expected)
    {
        var model = new RoomModel("wall", 1, 1, 0, 2, $"xxxx\rx0xx\rx{interiorHeight}{interiorHeight}x\rxxxx", 0, fixedHeight, true);
        Assert.Equal(expected, WiredWallGeometry.TileHeight(model, 0, 2));
    }

    [Theory]
    [InlineData("l", ":w=0,2 l=8,30 l a=200")]
    [InlineData("r", ":w=3,1 l=11,31 r a=200")]
    public void WallImplicitLegacyOriginUsesRoomScaleAndCanonicalCoordinates(string side, string expected)
    {
        WallSnapshotInstallStore();
        var model = new RoomModel("legacy", 1, 1, 0, 2, "xxxx\rx0xx\rx00x\rxxxx", 0, 0, true);
        Set("_gamemap", new Gamemap(_room, model, TestLogging.Navigation, _roomSettings,
            TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        _room.GetGameMap().GenerateMaps();
        var wall = WallSnapshotItem(301, $":w=0,0 l=11,16 {side}");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [2, 100], Text = "2", SelectedItems = [wall.Id] });
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(expected, wall.WallCoordinates);
    }

    [Fact]
    public void WallSnapshotSideChangeUsesAuthoritativeUpdateInsteadOfSingleSideAnimation()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,16 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_match_to_sshot", new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] });
        wall.WallCoordinates = ":w=8,0 l=12,57 r a=200";
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
        var context = WallSnapshotContext();
        context.Policy.Addons.DisableAnimation = false;
        _client.Packets.Clear();
        Assert.True(action.Execute(context));
        Assert.Equal(":w=3,7 l=12,16 l", wall.WallCoordinates);
        Assert.Equal(ServerPacketHeader.ItemUpdateComposer, Assert.Single(_client.Packets).Header);
    }

    [WiredChestDatabaseFact]
    public void WallProjectedRestorePersistsCapturedAltitudeAndExactAppendixAcrossReload()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("ALTER TABLE items ADD COLUMN wall_pos TEXT; ALTER TABLE wired_item_configurations ADD COLUMN schema_version INT NOT NULL DEFAULT 1");
        const string baseline = ":w=3,7 l=12,16 l";
        const string moved = ":w=3,8 l=20,57 l a=200";
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data,wall_pos) VALUES(301,7,42,0,'',@baseline)", new { baseline });
        Set("_roomItemHandling", new RoomItemHandling(_room, new RoomItemStore(fixture.Database), TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        WallGeometryModel();
        var wall = WallSnapshotItem(301, baseline);
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_match_to_sshot", new() { IntParams = [0, 0, 0, 1, 100], SelectedItems = [wall.Id] });
        var configurations = new WiredConfigurationStore(fixture.Database);
        Assert.True(WiredConfigurationSave.TrySave(action, action.Configuration, configurations,
            out var error, prepare: WiredRoomOperations.PrepareSnapshots), error);
        var saved = configurations.Load(action.Item.Id, action.Descriptor)!;
        Assert.Equal(341, Assert.Single(saved.Snapshots).Wall!.CapturedAltitudeHundredths);
        action.ApplyConfiguration(saved);
        wall.WallCoordinates = moved;
        fixture.Connection.Execute("UPDATE items SET wall_pos=@moved WHERE id=301", new { moved });
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
        var context = WallSnapshotContext();
        context.Policy.Addons.DisableAnimation = false;
        _client.Packets.Clear();
        fixture.Connection.Execute("CREATE TRIGGER reject_wall_projection BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reject wall projection'");
        Assert.Throws<MySqlConnector.MySqlException>(() => action.Execute(context));
        Assert.Equal(moved, wall.WallCoordinates);
        Assert.Equal(moved, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Empty(_client.Packets);
        fixture.Connection.Execute("DROP TRIGGER reject_wall_projection");
        Assert.True(action.Execute(context));
        const string restored = ":w=3,8 l=20,12 l a=341";
        Assert.Equal(restored, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Equal(ServerPacketHeader.WiredMovementsComposer, Assert.Single(_client.Packets).Header);
        var reloaded = WallSnapshotItem(301, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        _room.GetRoomItemHandler().LoadFurniture([reloaded]);
        var altitude = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [0, 100], Text = "0.01", SelectedItems = [reloaded.Id] });
        Assert.True(altitude.Execute(WallSnapshotContext()));
        Assert.Equal(":w=3,8 l=20,12 l a=342", fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Equal(":w=3,8 l=20,12 l a=342", reloaded.WallCoordinates);
    }

    [Theory]
    [InlineData(false, 500)]
    [InlineData(false, 750)]
    [InlineData(true, 750)]
    public void WallSnapshotPublishesNativeWallMovementWithActionAnimationPolicy(bool disabled, int duration)
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,16 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_match_to_sshot", new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] });
        wall.WallCoordinates = ":w=3,8 l=20,57 l a=200";
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
        var context = WallSnapshotContext();
        context.Policy.Addons.DisableAnimation = disabled;
        context.Policy.Addons.AnimationTimeMs = duration;
        _client.Packets.Clear();
        Assert.True(action.Execute(context));
        var packet = Assert.Single(_client.Packets);
        Assert.Equal(disabled ? ServerPacketHeader.ItemUpdateComposer : ServerPacketHeader.WiredMovementsComposer, packet.Header);

        if (!disabled) {
            var body = packet.Body;
            Assert.Equal(49, body.Length);
            Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(body));
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4)));
            Assert.Equal((int)wall.Id, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(8)));
            Assert.Equal(0, body[12]); // Left side.
            var expected = new[] { 3, 8, 20, 57, 3, 7, 12, 16, duration };
            Assert.Equal(expected, Enumerable.Range(0, 9).Select(index => BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(13 + index * 4))));
        }
    }

    private void WallGeometryModel(bool halfScale = false, int wallHeight = 0, double doorHeight = 0)
    {
        var model = new RoomModel("wall", 1, 1, doorHeight, 2,
            "xxxxxxxxxx\rx0xxxxxxxx\rx00000000x\rx00000000x\rx00000000x\rx00000000x\rx00000000x\rxxxxxxxxxx\rxxxxxxxxxx",
            0, wallHeight, true, presentation: new(halfScale, 0, 0, 0));
        Set("_gamemap", new Gamemap(_room, model, TestLogging.Navigation, _roomSettings,
            TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        _room.GetGameMap().GenerateMaps();
    }

    private WiredModernAction WallGeometryAction(string name, WiredConfiguration configuration)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var action = Assert.IsType<WiredModernAction>(_room.GetWired().CreateConfiguredBox(
            Furni(400, Plus.HabboHotel.Items.InteractionType.WiredEffect, WiredBoxType.None), descriptor));
        Assert.True(WiredConfigurationSave.TrySave(action, configuration, TestWiredConfigurationStore.Instance,
            out var error, prepare: WiredRoomOperations.PrepareSnapshots), error);

        return action;
    }
}
