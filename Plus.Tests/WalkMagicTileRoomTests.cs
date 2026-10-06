using System.Collections.Concurrent;
using System.Data;
using System.Drawing;
using Plus.HabboHotel.Rooms.Instance;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Revisions;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    private Item Add(uint id, int x, int y, double z = 0, double height = 0, bool stackable = true,
        InteractionType type = InteractionType.None, bool seat = false, int width = 1, int length = 1)
    {
        var item = Furni(id, type, WiredBoxType.None);
        item.UserId = 7;
        item.Definition.Height = height;
        item.Definition.Stackable = stackable;
        item.Definition.Width = width;
        item.Definition.Length = length;
        item.Definition.IsSeat = seat;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, x, y, 0, true, false, false, height: z));

        return item;
    }

    private RoomUser Viewer(int x = 3, int y = 3)
    {
        _client.GetHabbo().Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent(new FixedTimeProvider(FixedTimeProvider.Epoch));
        _client.GetHabbo().HabboStats = new Plus.HabboHotel.Users.HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        _client.GetHabbo().Inventory ??= new Plus.HabboHotel.Users.Inventory.InventoryComponent { Furniture = new Plus.HabboHotel.Users.Inventory.Furniture.FurnitureInventoryComponent([], []) };
        var user = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, new TestRewardProgress()) { X = x, Y = y };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        users.TryAdd(1, user);

        return user;
    }

    private MoveObjectEvent MoveObject() => new(PlacementService(() => { }));

    private async Task<Item?> Drop(uint id, int x, int y, InteractionType type, int width = 1, int length = 1)
    {
        var definition = Furni(id, type, WiredBoxType.None).Definition;
        definition.Width = width;
        definition.Length = length;
        definition.Height = 0;
        Inventory(new InventoryItem { Id = id, OwnerId = 7, Definition = definition });
        await PlaceObject().Parse(_room, _client, ClientPacket($"{id} {x} {y} 0"));

        return _room.GetRoomItemHandler().GetItem(id);
    }

    [Fact]
    public void WalkTileOverridesRaisedFurnitureBlockedBridgeAndSeatPosture()
    {
        Add(10, 1, 1, z: 5, height: 1, stackable: false);
        var tile = Add(11, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        Assert.Equal((byte)1, _room.GetGameMap().GameMap[1, 1]);
        Assert.Equal(0.75, _room.GetGameMap().SqAbsoluteHeight(1, 1));
        // Adding furniture after the helper must not restore collision.
        Add(12, 1, 1, z: 7, height: 2, stackable: false, type: InteractionType.Bed, seat: true);
        Assert.Equal((byte)1, _room.GetGameMap().GameMap[1, 1]);
        var user = Viewer(1, 1);
        user.Statusses["sit"] = "1";
        user.Statusses["lay"] = "1 null";
        user.IsSitting = user.IsLying = true;
        _room.GetRoomUserManager().UpdateUserStatus(user, false);
        Assert.Equal(0.75, user.Z);
        Assert.False(user.Statusses.ContainsKey("sit"));
        Assert.False(user.Statusses.ContainsKey("lay"));
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(tile, 1, 1, 10));
        Assert.Equal(10, user.Z);
        Assert.True(user.UpdateNeeded);
        Assert.Equal((byte)3, _room.GetGameMap().GameMap[0, 0]);
    }

    [Fact]
    public void HighestWalkTileThenHigherIdWinsAcrossEveryFootprintTile()
    {
        Add(10, 1, 1, z: 2, type: InteractionType.WalkMagicTile, length: 2);
        var high = Add(11, 1, 1, z: 3, type: InteractionType.WalkMagicTile, length: 2);
        var tie = Add(12, 1, 1, z: 3, type: InteractionType.WalkMagicTile, length: 2);
        Assert.Same(tie, _room.GetGameMap().WalkMagicAt(1, 1));
        Assert.Same(tie, _room.GetGameMap().WalkMagicAt(1, 2));
        _room.GetRoomItemHandler().SetFloorItem(tie, 1, 1, 1);
        Assert.Same(high, _room.GetGameMap().WalkMagicAt(1, 2));
        Assert.Equal(3, _room.GetGameMap().SqAbsoluteHeight(1, 2));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 3)]
    public void StacktoolCompatibilityControlsCollisionAndHeight(int setting, int state, double height)
    {
        _roomSettings.Values["pathfinding.stacktool_legacy_collision"] = setting.ToString();
        Add(10, 1, 1, z: 2, height: 1, type: InteractionType.Stacktool);
        Assert.Equal((byte)state, _room.GetGameMap().GameMap[1, 1]);
        Assert.Equal(height, _room.GetGameMap().SqAbsoluteHeight(1, 1));
        Assert.Equal(2, _room.GetGameMap().ResolvePlacement(1, 1).PlacementZ);
    }

    [Fact]
    public void StacktoolCompatibilitySettingRemainsLiveForAnExistingMap()
    {
        _roomSettings.Values["pathfinding.stacktool_legacy_collision"] = "1";
        var stacktool = Add(10, 1, 1, z: 2, height: 1, type: InteractionType.Stacktool);
        Assert.Equal((byte)0, _room.GetGameMap().GameMap[1, 1]);

        _roomSettings.Values["pathfinding.stacktool_legacy_collision"] = "0";
        _room.GetGameMap().UpdateMapForItem(stacktool);
        Assert.Equal((byte)1, _room.GetGameMap().GameMap[1, 1]);

        _roomSettings.Values["pathfinding.stacktool_legacy_collision"] = "1";
        _room.GetGameMap().UpdateMapForItem(stacktool);
        Assert.Equal((byte)0, _room.GetGameMap().GameMap[1, 1]);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 2)]
    public void StacktoolCompatibilityKeepsUnderlyingSupportWhenHelpersAreIgnored(int setting, int state, double height)
    {
        _roomSettings.Values["pathfinding.stacktool_legacy_collision"] = setting.ToString();
        var support = Add(10, 1, 1, height: 1);
        support.Definition.Walkable = true;
        _room.GetGameMap().UpdateMapForItem(support);
        Add(11, 1, 1, z: 2, type: InteractionType.Stacktool);
        Assert.Equal((byte)state, _room.GetGameMap().GameMap[1, 1]);
        Assert.Equal(height, _room.GetGameMap().SqAbsoluteHeight(1, 1));
    }

    [Fact]
    public void MissingStacktoolSettingDefaultsToLegacyCollision()
    {
        var settings = new SettingsManager(_database, NullLogger<SettingsManager>.Instance);
        Assert.Equal("1", settings.TryGetValue("pathfinding.stacktool_legacy_collision", "1"));
    }

    [Fact]
    public async Task RealPlacementAndMoveUseHelperPermissionAndMatchNonStackableTop()
    {
        Add(10, 1, 1, height: 2.35, stackable: false);
        Assert.Null(await Drop(11, 1, 1, InteractionType.None));
        var helper = await Drop(12, 1, 1, InteractionType.WalkMagicTile);
        Assert.NotNull(helper);
        Assert.Equal(2.35, helper.GetZ);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(12, 100));
        var rug = await Drop(13, 1, 1, InteractionType.None);
        Assert.NotNull(rug);
        Assert.Equal(1, rug.GetZ);
        Add(14, 2, 2, height: 3.5, stackable: false);
        await MoveObject().Parse(_room, _client, ClientPacket(12, 2, 2, 0));
        Assert.Equal(3.5, helper.GetZ);
        Assert.Equal("350", helper.LegacyDataString);
        Assert.Null(await Drop(15, 1, 1, InteractionType.None));
    }

    [Fact]
    public async Task MagicPlacementOnVoidIsStandableButNeverChangesFloorRendering()
    {
        var map = new Gamemap(_room, new RoomModel("void", 0, 0, 0, 0, "0000\r0x00\r0000\r0000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set("_gamemap", map);
        map.GenerateMaps();
        var floor = map.Model.GetRelativeHeightmap();
        Assert.Null(await Drop(10, 1, 1, InteractionType.None));
        var tile = await Drop(11, 1, 1, InteractionType.WalkMagicTile);
        Assert.NotNull(tile);
        Assert.Equal((byte)1, map.GameMap[1, 1]);
        Assert.Equal(0, map.SqAbsoluteHeight(1, 1));
        Assert.Equal(floor, map.Model.GetRelativeHeightmap());
        Assert.Equal(SquareState.Blocked, map.Model.SqState[1, 1]);
        Assert.NotNull(await Drop(12, 1, 1, InteractionType.None));
        // Ordinary furniture retains master's dynamic OpenSquare bridge behavior.
        Assert.Equal(SquareState.Open, map.Model.SqState[1, 1]);
        Assert.True(map.CanRollItemHere(1, 1));
        await MoveObject().Parse(_room, _client, ClientPacket(11, 2, 2, 0));
        // The floor opened by ordinary furniture stays open, as on master.
        await PickupObject()
            .Parse(_client, ClientPacket(0, 12));
        Assert.True(map.ResolvePlacement(1, 1).CanStack);
        Assert.Equal(SquareState.Open, map.Model.SqState[1, 1]);
        Assert.Equal((byte)1, map.GameMap[1, 1]);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-1, 1, 1, 1)]
    [InlineData(3, 3, 2, 2)]
    public async Task RealMagicPlacementRejectsDoorAndOutOfBoundsFootprints(int x, int y, int width, int length)
    {
        Assert.Null(await Drop(10, x, y, InteractionType.WalkMagicTile, width, length));
    }

    [Fact]
    public async Task HelperCoverageOnlyPermitsCoveredFootprintTiles()
    {
        Add(10, 1, 1, height: 2, stackable: false);
        Add(11, 1, 2, height: 3, stackable: false);
        Add(12, 1, 1, z: 1, type: InteractionType.Stacktool);
        Assert.Null(await Drop(13, 1, 1, InteractionType.None, length: 2));
        Add(14, 1, 2, z: 1.5, type: InteractionType.WalkMagicTile);
        var rug = await Drop(15, 1, 1, InteractionType.None, length: 2);
        Assert.NotNull(rug);
        Assert.Equal(1.5, rug.GetZ);
    }

    [Fact]
    public async Task WidgetMoveReloadAndRealObjectPacketsKeepAuthoritativeHeightAndPrivateSuffix()
    {
        var tile = await Drop(10, 1, 1, InteractionType.WalkMagicTile);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(10, 175, true));
        Assert.Equal("175;1", tile!.LegacyDataString);
        Add(11, 2, 2, height: 2.35);
        await MoveObject().Parse(_room, _client, ClientPacket(10, 2, 2, 0));
        Assert.Equal("235;1", tile.LegacyDataString);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(10, 300));
        Assert.Equal("300;1", tile.LegacyDataString);
        Assert.Equal(1, RoomItemSnapshot.Capture(tile).FloorExtra);

        foreach (var composer in new IServerPacket[] { new ObjectAddComposer(RoomItemSnapshot.Capture(tile)), new ObjectUpdateComposer(RoomItemSnapshot.Capture(tile)) }) {
            var packet = Body(composer);
            packet.ReadUInt();
            packet.ReadInt();
            packet.ReadInt();
            packet.ReadInt();
            packet.ReadInt();
            Assert.Equal("3", packet.ReadString());
            packet.ReadString();
            Assert.Equal(1, packet.ReadInt());
            Assert.Equal(0, packet.ReadInt());
            Assert.Equal("300", packet.ReadString());
        }

        var loaded = ItemLoader.ReadRoomItem(Row(3, tile.LegacyDataString), RoomId, tile.Definition);
        Assert.Equal("300;1", loaded.LegacyDataString);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(10, 325, false));
        Assert.Equal("325", tile.LegacyDataString);
        Assert.Equal(0, RoomItemSnapshot.Capture(tile).FloorExtra);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("100")]
    [InlineData("100;1")]
    public void RealLoaderNormalizesMagicPrefixesFromPhysicalZ(string stored)
    {
        var definition = Furni(10, InteractionType.WalkMagicTile, WiredBoxType.None).Definition;
        var item = ItemLoader.ReadRoomItem(Row(2, stored), RoomId, definition);
        Assert.Equal(stored.EndsWith(";1") ? "200;1" : "200", item.LegacyDataString);
        Assert.Equal(2, item.GetZ);
    }

    private static DataRow Row(double height, string stored)
    {
        var table = new DataTable();

        foreach (var column in new[] { "id", "user_id", "x", "y", "rot", "limited_number", "limited_stack" }) {
            table.Columns.Add(column, typeof(int));
        }

        table.Columns.Add("z", typeof(double));

        foreach (var column in new[] { "extra_data", "wall_pos", "username" }) {
            table.Columns.Add(column, typeof(string));
        }

        return table.Rows.Add(10, 7, 1, 1, 0, 0, 0, height, stored, "", "owner");
    }

    [Fact]
    public async Task WidgetRejectsNonMagicAndUnauthorizedAndClampsBothInputForms()
    {
        var ordinary = Add(10, 1, 1, height: 80);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(10, 100));
        Assert.Equal(0, ordinary.GetZ);
        var helper = Add(11, 1, 1, type: InteractionType.WalkMagicTile);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(11, -100));
        Assert.Equal(40, helper.GetZ);
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(11, 9000));
        Assert.Equal(40, helper.GetZ);
        _client.GetHabbo().Id = 99;
        _client.GetHabbo().Username = "visitor";
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(11, 0));
        Assert.Equal(40, helper.GetZ);
    }

    [Fact]
    public async Task AdjacentPacketTranslates2687RegistersAndEchoes2816()
    {
        Viewer();
        var tile = Add(10, 1, 1, z: 2, type: InteractionType.WalkMagicTile);
        var path = Path.Combine(AppContext.BaseDirectory, "revisions", "OCTANE-3-6-0-FLOOR-20260909.json");
        var revision = JsonSerializer.Deserialize<Revision>(File.ReadAllText(path))!;
        revision.InternalIdToOutgoingIdMapping = revision.OutgoingHeaders.Where(pair => pair.Value > 0).ToDictionary(pair => (uint)typeof(ServerPacketHeader).GetField(pair.Key)!.GetRawConstantValue()!, pair => pair.Value);
        _client.Revision = revision;
        var mapping = revision.IncomingHeaders.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Value, pair => (uint)typeof(ClientPacketHeader).GetField(pair.Key)!.GetRawConstantValue()!);
        using var manager = new PacketManager([new UpdateMagicTileAdjacentEvent(new MagicTileService())], NullLogger<PacketManager>.Instance);
        await manager.TryExecutePacket(_client, mapping[2687], ClientPacket(10, false));
        Assert.Equal(2.01, tile.GetZ);
        Assert.Contains(2816u, _client.Sent);
        var echo = new FlashIncomingPacket { Buffer = _client.Packets.Last(packet => packet.Header == 2816).Body };
        Assert.Equal(10u, echo.ReadUInt());
        Assert.Equal(201, echo.ReadInt());
        await manager.TryExecutePacket(_client, mapping[2687], ClientPacket(10, true));
        Assert.Equal(2, tile.GetZ);
    }

    [Fact]
    public async Task FullRoomEntryAndDeltasAgreeAfterOrdinaryTableMovePickupAndBlockerRemoval()
    {
        Viewer();
        var table = Add(10, 1, 1, height: 1.25);
        Assert.Equal((short)320, DeltaAt(1, 1));
        await MoveObject().Parse(_room, _client, ClientPacket(10, 2, 2, 0));
        Assert.Equal((short)0, DeltaAt(1, 1));
        Assert.Equal((short)320, DeltaAt(2, 2));
        _room.SendObjects(_client);
        var full = new FlashIncomingPacket { Buffer = _client.Packets.Last(packet => packet.Header == ServerPacketHeader.HeightMapComposer).Body };
        var width = full.ReadInt();
        var count = full.ReadInt();

        for (var index = 0; index < count; index++) {
            Assert.Equal(_room.GetGameMap().PlacementHeightMap()[index % width, index / width], full.ReadShort());
        }

        await PickupObject()
            .Parse(_client, ClientPacket(0, 10));
        Assert.Equal((short)0, DeltaAt(2, 2));
        Add(11, 1, 1, height: 2, stackable: false);
        Assert.Equal((short)(512 | 0x4000), DeltaAt(1, 1));
        await PickupObject()
            .Parse(_client, ClientPacket(0, 11));
        Assert.Equal((short)0, DeltaAt(1, 1));
    }

    [Fact]
    public async Task HelperMoveAndRemovalRestoreUnderlyingStackAndStateChangesProject()
    {
        Viewer();
        var table = Add(10, 1, 1, height: 3, stackable: false);
        var helper = Add(11, 1, 1, z: 1, type: InteractionType.WalkMagicTile);
        Assert.Equal((short)256, DeltaAt(1, 1));
        await MoveObject().Parse(_room, _client, ClientPacket(11, 2, 2, 0));
        Assert.Equal((short)(768 | 0x4000), DeltaAt(1, 1));
        Assert.Equal((short)0, _room.GetGameMap().PlacementHeightMap()[2, 2]);
        table.Definition.AdjustableHeights = [3, 4.5];
        table.ExtraData = new LegacyDataFormat { Data = "1" };
        table.UpdateState();
        Assert.Equal((short)(1152 | 0x4000), DeltaAt(1, 1));
        await new UpdateMagicTileEvent(new MagicTileService()).Parse(_client, ClientPacket(11, 225));
        Assert.Equal((short)576, DeltaAt(2, 2));
        await PickupObject()
            .Parse(_client, ClientPacket(0, 11));
        Assert.Equal((short)0, DeltaAt(2, 2));
    }

    [Fact]
    public void LegacyMovementEmitsWalkTileZInMvUnderRaisedTable()
    {
        Add(10, 1, 1, z: 5, height: 1, stackable: false);
        Add(11, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        var user = Viewer(1, 0);
        user.Path = [new(1, 1), new(1, 0)];
        user.PathStep = 1;
        user.GoalX = user.GoalY = 1;
        user.IsWalking = true;
        _room.GetRoomUserManager().OnCycle();
        Assert.Equal("1,1,0.75", user.Statusses["mv"]);
        Assert.Equal(0.75, user.SetZ);
        var status = Body(new UserUpdateComposer(RoomUserStatusSnapshot.Capture([user])));
        Assert.Equal(1, status.ReadInt());
        status.ReadInt();
        status.ReadInt();
        status.ReadInt();
        status.ReadString();
        status.ReadInt();
        status.ReadInt();
        Assert.Contains("/mv 1,1,0.75/", status.ReadString());
    }

    [Fact]
    public void RollerAndWiredMovesSyncDataAndPublishBothFootprints()
    {
        Viewer();
        var tile = Add(10, 1, 1, z: 2, type: InteractionType.WalkMagicTile);
        tile.ExtraData = new LegacyDataFormat { Data = "200;1" };
        // Invoke the same relocation path used by CycleRollers.
        _room.GetRoomItemHandler().UpdateItemOnRoller(tile, new(1, 2), 42, 1.75);
        Assert.Equal("175;1", tile.LegacyDataString);
        Assert.Equal((short)0, DeltaAt(1, 1));
        Assert.Equal((short)448, DeltaAt(1, 2));
        Assert.True(Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.MoveItem(_room, tile, 2, 2, height: 1.25, animate: false));
        Assert.Equal("125;1", tile.LegacyDataString);
        Assert.Equal((short)0, DeltaAt(1, 2));
        Assert.Equal((short)320, DeltaAt(2, 2));
    }

    [Fact]
    public void RoomReloadRepairsStalePrefixAndSaveFurniturePersistsBothZAndSuffix()
    {
        var row = Row(2, "100;1");
        row.Table.Columns.Add("base_item", typeof(int));
        row["base_item"] = 10;
        var definition = Furni(10, InteractionType.WalkMagicTile, WiredBoxType.None).Definition;
        definition.Width = definition.Length = 1;
        definition.Height = 0;
        var store = new RecordingRoomItemStore();
        Set("_roomItemHandling", new RoomItemHandling(_room, store, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        _room.GetRoomItemHandler().LoadFurniture([ItemLoader.ReadRoomItem(row, RoomId, definition)]);
        var tile = _room.GetRoomItemHandler().GetItem(10);
        Assert.Equal("200;1", tile.LegacyDataString);
        typeof(RoomItemHandling).GetMethod("SaveFurniture", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_room.GetRoomItemHandler(), null);
        var saved = Assert.Single(store.Saved);
        Assert.Equal("200;1", saved.ExtraData);
        Assert.Equal(2, saved.Z);
        // The persistent prefix and physical altitude survive another real room load.
        row["extra_data"] = saved.ExtraData!;
        _room.GetRoomItemHandler().LoadFurniture([ItemLoader.ReadRoomItem(row, RoomId, definition)]);
        Assert.Equal("200;1", _room.GetRoomItemHandler().GetItem(10).LegacyDataString);
    }

    [Fact]
    public void LegacyWiredRotationUpdatesOldAndNewRectangleProjection()
    {
        Viewer();
        var table = Add(10, 1, 1, height: 2, length: 2);
        var box = new Plus.HabboHotel.Items.Wired.Boxes.Effects.MatchPositionBox(_room, Furni(12, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition))
        {
            StringData = "0;1;0",
            ItemsData = "10:1,1,0,2,0"
        };
        box.SetItems.TryAdd(10, table);
        Assert.True(box.Execute());
        Assert.Equal(2, table.Rotation);
        Assert.Equal((short)0, DeltaAt(1, 2));
        Assert.Equal((short)512, DeltaAt(2, 1));
        Assert.Contains(table, _room.GetGameMap().GetCoordinatedItems(new(2, 1)));
        Assert.DoesNotContain(table, _room.GetGameMap().GetCoordinatedItems(new(1, 2)));
    }

    [Fact]
    public void MovementKeepsLegacyEffectHookWhileHeightRebindingIsCallbackFree()
    {
        var effect = Add(10, 1, 1, type: InteractionType.Effect);
        effect.Definition.EffectId = 7;
        effect.ExtraData = new LegacyDataFormat { Data = "0" };
        var tile = Add(11, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        var user = Viewer(1, 1);
        _client.GetHabbo().Effects.Init(_client.GetHabbo());
        _room.GetRoomUserManager().UpdateUserStatus(user, true);
        Assert.Equal(7, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("1", effect.LegacyDataString);
        effect.LegacyDataString = "0";
        _client.GetHabbo().Effects.CurrentEffect = 0;
        _room.GetRoomItemHandler().SetFloorItem(tile, 1, 1, 1);
        Assert.Equal(1, user.Z);
        Assert.Equal("0", effect.LegacyDataString);
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
    }

    [Fact]
    public void OlderRevisionViewerReceivesFullProjectionWithoutChangingItsHeaders()
    {
        Viewer();
        _client.Revision.InternalIdToOutgoingIdMapping = _client.Revision.InternalIdToOutgoingIdMapping
            .Where(pair => pair.Key != ServerPacketHeader.HeightMapUpdateComposer).ToDictionary();
        Add(10, 1, 1, height: 1.25);
        Assert.DoesNotContain(ServerPacketHeader.HeightMapUpdateComposer, _client.Sent);
        var full = new FlashIncomingPacket { Buffer = _client.Packets.Last(packet => packet.Header == ServerPacketHeader.HeightMapComposer).Body };
        Assert.Equal(4, full.ReadInt());
        Assert.Equal(16, full.ReadInt());

        for (var index = 0; index < 5; index++) {
            full.ReadShort();
        }

        Assert.Equal((short)320, full.ReadShort());
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void WalkMagicFlanksProvideAnOpenSurfaceAtTheirOwnHeight(double flankHeight)
    {
        var map = new Gamemap(_room, new RoomModel("flanks", 0, 0, 0, 0, "0000\r00x0\r0000\r0000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set("_gamemap", map);
        map.GenerateMaps();
        Add(10, 1, 2, height: 5, stackable: false);
        Add(11, 1, 2, z: 0.5, type: InteractionType.WalkMagicTile);
        Add(12, 2, 1, z: flankHeight, type: InteractionType.WalkMagicTile);
        Assert.Equal(flankHeight, map.SqAbsoluteHeight(2, 1));
        Assert.Equal((byte)1, map.GameMap[2, 1]);
    }

    [Fact]
    public void OrdinaryFurnitureKeepsLegacyRollerSupportOverModelVoid()
    {
        var map = new Gamemap(_room, new RoomModel("bridge", 0, 0, 0, 0, "0000\r0x00\r0000\r0000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set("_gamemap", map);
        map.GenerateMaps();
        Assert.False(map.CanRollItemHere(1, 1));
        var bridge = Add(10, 1, 1, z: 2, height: 0.5);
        bridge.Definition.Walkable = true;
        map.UpdateMapForItem(bridge);
        Assert.Equal(SquareState.Open, map.Model.SqState[1, 1]);
        Assert.Equal(2, map.Model.SqFloorHeight[1, 1]);
        Assert.True(map.CanRollItemHere(1, 1));
        Assert.Equal(2.5, map.SqAbsoluteHeight(1, 1));
        var passenger = Add(11, 2, 1, z: 3);
        _room.GetRoomItemHandler().UpdateItemOnRoller(passenger, new(1, 1), 42, 2.5);
        Assert.Equal(1, passenger.GetX);
        Assert.Equal(2.5, passenger.GetZ);
    }

    [Fact]
    public async Task RotationKeepsUnsupportedHeightButRisesToNewSupportAndUsesHelperHeight()
    {
        var support = Add(10, 1, 1, height: 3);
        var item = await Drop(11, 1, 1, InteractionType.None);
        Assert.Equal(3, item!.GetZ);
        await PickupObject()
            .Parse(_client, ClientPacket(0, (int)support.Id));
        await MoveObject().Parse(_room, _client, ClientPacket(11, 1, 1, 2));
        Assert.Equal(3, item.GetZ);
        Add(12, 1, 1, height: 4);
        await MoveObject().Parse(_room, _client, ClientPacket(11, 1, 1, 4));
        Assert.Equal(4, item.GetZ);
        Add(13, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        await MoveObject().Parse(_room, _client, ClientPacket(11, 1, 1, 6));
        Assert.Equal(0.75, item.GetZ);
    }

    [Theory]
    [InlineData(InteractionType.None, true, false, true)]
    [InlineData(InteractionType.None, false, false, false)]
    [InlineData(InteractionType.WalkMagicTile, false, false, true)]
    [InlineData(InteractionType.Stacktool, false, false, true)]
    [InlineData(InteractionType.None, true, true, false)]
    [InlineData(InteractionType.WalkMagicTile, true, true, false)]
    public void WiredCollisionRespectsHelperCoverageAndExplicitBlockers(InteractionType type, bool helper, bool explicitBlock, bool expected)
    {
        var blocker = Add(10, 2, 2, height: 2, stackable: false);

        if (helper) {
            Add(11, 2, 2, z: 0.75, type: InteractionType.WalkMagicTile);
        }

        var item = Add(12, 1, 1, type: type);
        var policy = new Plus.HabboHotel.Items.Wired.Modern.WiredCollisionPolicy(
            new HashSet<uint>(), new HashSet<int>(), explicitBlock ? new HashSet<uint> { blocker.Id } : new HashSet<uint>());
        Assert.Equal(expected, Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.MoveItem(_room, item, 2, 2, animate: false, collision: policy));
        Assert.Equal(expected ? 2 : 1, item.GetX);

        if (expected) {
            Assert.Equal(helper ? 0.75 : 2, item.GetZ);
        }
    }

    [Theory]
    [InlineData(true, "1,1,0.75")]
    [InlineData(false, "1,1,1.75")]
    public void MountedMovementAlwaysEmitsHorseMvAndOnlyOffsetsRiderWithoutWalkTile(bool walkTile, string riderMv)
    {
        if (walkTile) {
            Add(10, 1, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        }
        else {
            var support = Add(10, 1, 1, height: 0.75);
            support.Definition.Walkable = true;
            _room.GetGameMap().UpdateMapForItem(support);
        }

        var rider = Viewer(1, 0);
        var horse = new RoomUser(0, RoomId, 0, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)
        {
            X = 1,
            Y = 0,
            RidingHorse = true,
            BotData = (Plus.HabboHotel.Rooms.AI.RoomBot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plus.HabboHotel.Rooms.AI.RoomBot))
        };
        var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        roster[0] = horse;
        rider.RidingHorse = true;
        rider.HorseId = horse.VirtualId;
        rider.Path = [new(1, 1), new(1, 0)];
        rider.PathStep = 1;
        rider.GoalX = rider.GoalY = 1;
        rider.IsWalking = true;
        _room.GetRoomUserManager().OnCycle();
        Assert.Equal(riderMv, rider.Statusses["mv"]);
        Assert.Equal("1,1,0.75", horse.Statusses["mv"]);
        Assert.True(horse.UpdateNeeded);
    }

    [Fact]
    public async Task ConcurrentProjectionFlushesDeliverSnapshotsInOrder()
    {
        Viewer();
        var table = Add(10, 1, 1);
        using var sending = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var laterReady = new ManualResetEventSlim();
        var sends = 0;
        _client.BeforeCapture = header =>
        {
            if (header != ServerPacketHeader.HeightMapUpdateComposer || Interlocked.Increment(ref sends) != 1) {
                return;
            }

            sending.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        var first = Task.Run(() =>
        {
            table.Definition.Height = 1;
            table.UpdateState();
        });
        Task? later = null;

        try {
            Assert.True(sending.Wait(TimeSpan.FromSeconds(10)));
            later = Task.Run(() =>
            {
                table.Definition.Height = 2;
                laterReady.Set();
                table.UpdateState();
            });
            Assert.True(laterReady.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(later.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, Volatile.Read(ref sends));
        }
        finally {
            release.Set();
        }

        await first;

        if (later != null) {
            await later;
        }

        Assert.Equal(2, sends);
        var values = _client.Packets.Where(packet => packet.Header == ServerPacketHeader.HeightMapUpdateComposer).Select(packet =>
        {
            var body = new FlashIncomingPacket { Buffer = packet.Body.ToArray() };
            Assert.Equal(1, body.ReadByte());
            Assert.Equal(1, body.ReadByte());
            Assert.Equal(1, body.ReadByte());

            return body.ReadShort();
        }).ToArray();
        Assert.Equal(new short[] { 256, 512 }, values);
        Assert.Equal((short)512, _room.GetGameMap().PlacementHeightMap()[1, 1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedProjectionDeliveryAllowsBridgeConstructionAndMapRebuild(bool rebuild)
    {
        var map = new Gamemap(_room, new RoomModel("bridge", 0, 0, 0, 0, "0000\r0x00\r0000\r0000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set("_gamemap", map);
        map.GenerateMaps();
        var bridge = Add(10, 2, 1, z: 2, height: 0.5);
        var table = Add(11, 3, 2);
        Viewer();
        using var sending = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var mutationReady = new ManualResetEventSlim();
        var sends = 0;
        _client.BeforeCapture = header =>
        {
            if (header != ServerPacketHeader.HeightMapUpdateComposer || Interlocked.Increment(ref sends) != 1) {
                return;
            }

            sending.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        var first = Task.Run(() =>
        {
            table.Definition.Height = 1;
            table.UpdateState();
        });
        Task? mutation = null;

        try {
            Assert.True(sending.Wait(TimeSpan.FromSeconds(10)));
            mutation = Task.Run(() =>
            {
                mutationReady.Set();

                if (rebuild) {
                    map.GenerateMaps();
                }
                else {
                    _room.GetRoomItemHandler().UpdateItemOnRoller(bridge, new(1, 1), 42, 2);
                }

                map.FlushPlacementUpdates();
            });
            Assert.True(mutationReady.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(mutation.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(rebuild ? SquareState.Blocked : SquareState.Open, map.Model.SqState[1, 1]);
            Assert.Equal(1, Volatile.Read(ref sends));
        }
        finally {
            release.Set();
        }

        await first;

        if (mutation != null) {
            await mutation;
        }

        Assert.Equal(rebuild ? SquareState.Blocked : SquareState.Open, map.Model.SqState[1, 1]);
        Assert.Equal((short)256, DeltaAt(3, 2));

        if (!rebuild) {
            Assert.True(map.CanRollItemHere(1, 1));
            Assert.Equal((short)640, DeltaAt(1, 1));
            Assert.Equal((short)640, map.PlacementHeightMap()[1, 1]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectionSendReenteringDisconnectCannotDeadlockWiredMovement(bool entry)
    {
        var actor = Viewer();
        var table = Add(10, 1, 1);
        var tile = Add(11, 2, 1, z: 0.75, type: InteractionType.WalkMagicTile);
        var map = _room.GetGameMap();
        var wired = _room.GetWired();
        var engine = typeof(WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        var wiredSync = engine.GetType().GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        using var wiredHeld = new ManualResetEventSlim();
        using var sending = new ManualResetEventSlim();
        using var moving = new ManualResetEventSlim();
        var callbacks = 0;
        var cleanupCompleted = false;
        var sentUnderLock = false;
        _client.SendCallback = _ =>
        {
            if (Interlocked.Increment(ref callbacks) != 1) {
                return false;
            }

            sentUnderLock = Monitor.IsEntered(map.PlacementSync);
            sending.Set();

            if (!moving.Wait(TimeSpan.FromSeconds(10))) {
                return false;
            }

            // Bound the failed case, but exercise the real lock and disconnect cleanup path.
            if (!Monitor.TryEnter(wiredSync, TimeSpan.FromSeconds(5))) {
                return false;
            }

            try {
                wired.BeforeActorLeaves(actor);
                cleanupCompleted = true;
            }
            finally {
                Monitor.Exit(wiredSync);
            }

            return false;
        };
        var movement = Task.Run(() =>
        {
            lock (wiredSync) {
                wiredHeld.Set();
                Assert.True(sending.Wait(TimeSpan.FromSeconds(10)));
                moving.Set();
                Assert.True(Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.MoveItem(
                    _room, tile, 2, 2, keepAltitude: true, animate: false, announce: false));
            }
        });
        Assert.True(wiredHeld.Wait(TimeSpan.FromSeconds(10)));
        var send = Task.Run(() =>
        {
            if (entry) {
                map.SendPlacementHeightMap(_client);
            }
            else {
                table.Definition.Height = 1;
                table.UpdateState();
            }
        });
        await Task.WhenAll(movement, send).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(sentUnderLock);
        Assert.True(cleanupCompleted);
        Assert.Equal(2, tile.GetY);
        Assert.Null(map.WalkMagicAt(2, 1));
        Assert.Same(tile, map.WalkMagicAt(2, 2));
        Assert.Equal((short)192, DeltaAt(2, 2));
    }

    [Fact]
    public async Task SupersededBroadcastsKeepAllTilesAndEntrySnapshotsStayOrdered()
    {
        Viewer();
        var firstTable = Add(10, 1, 1);
        var secondTable = Add(11, 2, 1);
        var thirdTable = Add(12, 1, 2);
        using var sending = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var captured = 0;
        _client.BeforeCapture = header =>
        {
            if (header != ServerPacketHeader.HeightMapUpdateComposer || Interlocked.Increment(ref captured) != 1) {
                return;
            }

            sending.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        var first = Task.Run(() =>
        {
            firstTable.Definition.Height = 1;
            firstTable.UpdateState();
        });

        try {
            Assert.True(sending.Wait(TimeSpan.FromSeconds(10)));
            secondTable.Definition.Height = 2;
            secondTable.UpdateState();
            thirdTable.Definition.Height = 3;
            thirdTable.UpdateState();
            _room.GetGameMap().SendPlacementHeightMap(_client);
            secondTable.Definition.Height = 4;
            secondTable.UpdateState();
        }
        finally {
            release.Set();
        }

        await first.WaitAsync(TimeSpan.FromSeconds(15));
        var packets = _client.Packets.Where(packet => packet.Header is ServerPacketHeader.HeightMapComposer or ServerPacketHeader.HeightMapUpdateComposer).ToArray();
        Assert.Equal(new uint[] { ServerPacketHeader.HeightMapUpdateComposer, ServerPacketHeader.HeightMapComposer,
            ServerPacketHeader.HeightMapComposer, ServerPacketHeader.HeightMapUpdateComposer }, packets.Select(packet => packet.Header));

        foreach (var packet in packets.Skip(1).Take(2)) {
            var full = new FlashIncomingPacket { Buffer = packet.Body.ToArray() };
            Assert.Equal(4, full.ReadInt());
            Assert.Equal(16, full.ReadInt());
            var heights = Enumerable.Range(0, 16).Select(_ => full.ReadShort()).ToArray();
            Assert.Equal((short)256, heights[5]);
            Assert.Equal((short)512, heights[6]);
            Assert.Equal((short)768, heights[9]);
        }

        Assert.Equal((short)1024, DeltaAt(2, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapRebuildAtRemovalBoundaryCannotRestoreMovedHelpersOldFootprint(bool secondary)
    {
        Add(10, 1, 1, height: 1, stackable: false);
        var tile = Add(11, 1, 1, z: 2, type: InteractionType.WalkMagicTile, length: 2);
        var map = _room.GetGameMap();
        var coordinates = (ConcurrentDictionary<Point, List<uint>>)typeof(Gamemap)
            .GetField("_coordinatedItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map)!;
        var oldIds = coordinates[new(1, 1)];
        using var rebuilding = new ManualResetEventSlim();
        Task? movement = null;
        Task? rebuild = null;
        Monitor.Enter(oldIds);

        try {
            // RemoveFromMap drops the helper ID before reading the remaining blocker under this lock.
            // Pause that actual handler exactly between old-index removal and SetState/new-index insertion.
            movement = Task.Run(() =>
            {
                if (secondary) {
                    Assert.True(_room.GetRoomItemHandler().SetFloorItem(tile, 2, 2, 2));
                }
                else {
                    Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, tile, 2, 2, 2, false, false, false, height: 2));
                }
            });
            Assert.True(SpinWait.SpinUntil(() => !oldIds.Contains(tile.Id), TimeSpan.FromSeconds(10)));
            Assert.Equal(1, tile.GetX);
            rebuild = Task.Run(() =>
            {
                rebuilding.Set();
                map.GenerateMaps();
            });
            Assert.True(rebuilding.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(rebuild.Wait(TimeSpan.FromMilliseconds(100)));
        }
        finally {
            Monitor.Exit(oldIds);
        }

        if (movement != null) {
            await movement.WaitAsync(TimeSpan.FromSeconds(15));
        }

        if (rebuild != null) {
            await rebuild.WaitAsync(TimeSpan.FromSeconds(15));
        }

        Assert.Equal(2, tile.GetX);
        Assert.Equal(2, tile.GetY);
        Assert.Null(map.WalkMagicAt(1, 1));
        Assert.Null(map.WalkMagicAt(1, 2));
        Assert.False(map.ResolvePlacement(1, 1).HasHelper);
        Assert.False(map.ResolvePlacement(1, 1).CanStack);
        Assert.DoesNotContain(tile, map.GetCoordinatedItems(new(1, 1)));

        foreach (var point in tile.GetCoords) {
            Assert.Same(tile, map.WalkMagicAt(point.X, point.Y));
        }
    }

    [Fact]
    public async Task ConcurrentPlacementOfSameIdRegistersOneFootprintWithoutGhostHelpers()
    {
        var first = Furni(10, InteractionType.WalkMagicTile, WiredBoxType.None);
        var second = Furni(10, InteractionType.WalkMagicTile, WiredBoxType.None);
        first.Definition.Width = first.Definition.Length = second.Definition.Width = second.Definition.Length = 1;
        var map = _room.GetGameMap();
        using var starting = new CountdownEvent(2);
        Thread? firstThread = null;
        Thread? secondThread = null;
        Task firstPlacement;
        Task secondPlacement;

        lock (map.PlacementSync) {
            firstPlacement = Task.Factory.StartNew(() =>
            {
                firstThread = Thread.CurrentThread;
                starting.Signal();
                Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, first, 1, 1, 0, true, false, false));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            secondPlacement = Task.Factory.StartNew(() =>
            {
                secondThread = Thread.CurrentThread;
                starting.Signal();
                Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, second, 2, 2, 0, true, false, false));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(starting.Wait(TimeSpan.FromSeconds(10)));
            // Both real placement handlers are waiting on the commit lock before either can register.
            Assert.True(SpinWait.SpinUntil(() => (firstThread!.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0
                && (secondThread!.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)));
        }

        await Task.WhenAll(firstPlacement, secondPlacement).WaitAsync(TimeSpan.FromSeconds(15));
        var placed = Assert.Single(_room.GetRoomItemHandler().GetFloor);
        Assert.Contains(placed, new[] { first, second });
        Assert.Same(placed, map.WalkMagicAt(placed.GetX, placed.GetY));
        var unused = placed == first ? new Point(2, 2) : new Point(1, 1);
        Assert.Null(map.WalkMagicAt(unused.X, unused.Y));
        Assert.False(map.ResolvePlacement(unused.X, unused.Y).HasHelper);
        Assert.Empty(map.GetCoordinatedItems(unused));
    }

    [Fact]
    public async Task VisitorEnteringDuringPausedRecipientResolutionReceivesTheFollowingMutation()
    {
        var table = Add(10, 1, 1, height: 1);
        var map = _room.GetGameMap();
        var existingClient = new TestClient();
        existingClient.SetHabbo(new Plus.HabboHotel.Users.Habbo { Id = 8, CurrentRoom = _room });
        var existingVisit = new RoomUser(8, RoomId, 0, _room, existingClient, TestChatEmotions.Unused, TestRewardProgress.Unused); // Bind this visit to its admitted client.
        var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        roster[0] = existingVisit;
        using var resolving = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var sends = 0;
        var sendUnderLock = false;
        existingClient.BeforeCapture = _ =>
        {
            if (Interlocked.Increment(ref sends) == 1) {
                sendUnderLock = Monitor.IsEntered(map.PlacementSync);
                resolving.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
        };
        table.Definition.Height = 2;
        map.AddItemToMap(table, false); // Like a furniture commit, dirty the footprint before the flush.
        var flush = Task.Run(map.FlushPlacementUpdates);

        try {
            Assert.True(resolving.Wait(TimeSpan.FromSeconds(10)));
            Viewer();
            map.SendPlacementHeightMap(_client);
            table.Definition.Height = 3;
            map.AddItemToMap(table, false);
        }
        finally {
            release.Set();
        }

        await flush.WaitAsync(TimeSpan.FromSeconds(15));
        map.FlushPlacementUpdates();
        Assert.False(sendUnderLock);
        var entry = Assert.Single(_client.Packets.Where(packet => packet.Header == ServerPacketHeader.HeightMapComposer));
        var full = new FlashIncomingPacket { Buffer = entry.Body.ToArray() };
        Assert.Equal(4, full.ReadInt());
        Assert.Equal(16, full.ReadInt());
        var heights = Enumerable.Range(0, 16).Select(_ => full.ReadShort()).ToArray();
        Assert.Equal((short)512, heights[5]);
        Assert.Equal((short)768, DeltaAt(1, 1));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("reenter")]
    [InlineData("unload")]
    [InlineData("disconnect")]
    public async Task BlockedPlacementDrainDiscardsMapsFromEndedRoomVisits(string transition)
    {
        var table = Add(10, 1, 1, height: 1);
        Viewer();
        var map = _room.GetGameMap();
        var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        var blocker = new TestClient();
        blocker.SetHabbo(new Plus.HabboHotel.Users.Habbo { Id = 8, CurrentRoom = _room });
        var blockingVisit = new RoomUser(8, RoomId, 0, _room, blocker, TestChatEmotions.Unused, TestRewardProgress.Unused);
        roster[0] = blockingVisit;
        using var sending = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var sends = 0;
        blocker.SendCallback = _ =>
        {
            if (Interlocked.Increment(ref sends) == 1) {
                sending.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }

            return false;
        };
        // Hold one different client's send so every map for the tested client remains queued.
        var drain = Task.Run(() => map.SendPlacementHeightMap(blocker));

        try {
            Assert.True(sending.Wait(TimeSpan.FromSeconds(10)));
            map.SendPlacementHeightMap(_client);
            table.Definition.Height = 2;
            table.UpdateState();

            switch (transition) {
                case "switch": {
                        EnterProjectionRoom();
                        break;
                    }
                case "reenter": {
                        _client.GetHabbo().CurrentRoom = null;
                        roster.TryRemove(1, out _);
                        var nextVisit = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused); // Same virtual ID, different visit identity.
                        roster[1] = nextVisit;
                        _client.GetHabbo().CurrentRoom = _room;
                        table.Definition.Height = 3;
                        map.SendPlacementHeightMap(_client);
                        break;
                    }
                case "unload":
                    map.Dispose();
                    map.SendPlacementHeightMap(_client); // Closed queues also reject future entry/flush requests.
                    map.FlushPlacementUpdates();
                    break;
                case "disconnect":
                    _client.IsAuthenticated = false;
                    break;
            }
        }
        finally {
            release.Set();
        }

        await drain.WaitAsync(TimeSpan.FromSeconds(15));
        var packets = _client.Packets.Where(packet => packet.Header is ServerPacketHeader.HeightMapComposer or ServerPacketHeader.HeightMapUpdateComposer).ToArray();

        if (transition is "unload" or "disconnect") {
            Assert.Empty(packets);
        }
        else {
            var packet = Assert.Single(packets);
            Assert.Equal(ServerPacketHeader.HeightMapComposer, packet.Header);
            var full = new FlashIncomingPacket { Buffer = packet.Body.ToArray() };
            Assert.Equal(4, full.ReadInt());
            Assert.Equal(16, full.ReadInt());
            var heights = Enumerable.Range(0, 16).Select(_ => full.ReadShort()).ToArray();

            if (transition == "switch") {
                Assert.All(heights, value => Assert.Equal((short)256, value));
            }
            else {
                Assert.Equal((short)768, heights[5]);
            }
        }
    }

    private Gamemap EnterProjectionRoom()
    {
        var nextRoom = (Room)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Room));
        nextRoom.Id = RoomId + 1;
        var nextUsers = new RoomUserManager(nextRoom, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        var nextMap = new Gamemap(nextRoom, new RoomModel("next", 0, 0, 0, 0, "1111\r1111\r1111\r1111", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(nextRoom, nextUsers);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(nextRoom, new RoomItemHandling(nextRoom, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(nextRoom, nextMap);
        nextMap.GenerateMaps();
        _client.GetHabbo().CurrentRoom = nextRoom;
        var nextVisit = new RoomUser(7, nextRoom.Id, 1, nextRoom, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var nextRoster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(nextUsers)!;
        nextRoster[1] = nextVisit;
        nextMap.SendPlacementHeightMap(_client);

        return nextMap;
    }

    [Fact]
    public void VisitChangeDuringEncodingDropsTheOldMapAtTheTransportBoundary()
    {
        Add(10, 1, 1, height: 2);
        Viewer();
        var sent = new List<byte[]>();
        var encodings = 0;
        _client.SendCallback = args => { sent.Add(args.MemoryBuffer.Slice(6).ToArray()); return false; };
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.HeightMapComposer && Interlocked.Increment(ref encodings) == 1) {
                EnterProjectionRoom();
            }
        };
        _room.GetGameMap().SendPlacementHeightMap(_client);
        // Encoding A re-enters B and sends B's full map before A reaches its transport callback.
        var body = new FlashIncomingPacket { Buffer = Assert.Single(sent) };
        Assert.Equal(4, body.ReadInt());
        Assert.Equal(16, body.ReadInt());

        for (var index = 0; index < 16; index++) {
            Assert.Equal((short)256, body.ReadShort());
        }

        Assert.Equal(2, encodings);
    }

    private short DeltaAt(int x, int y)
    {
        foreach (var sent in _client.Packets.Where(packet => packet.Header == ServerPacketHeader.HeightMapUpdateComposer).Reverse()) {
            var packet = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
            var count = packet.ReadByte();

            for (var index = 0; index < count; index++) {
                var tx = packet.ReadByte();
                var ty = packet.ReadByte();
                var value = packet.ReadShort();

                if (tx == x && ty == y) {
                    return value;
                }
            }
        }

        throw new InvalidOperationException($"No delta for {x},{y}");
    }

    private static FlashIncomingPacket Body(IServerPacket composer)
    {
        using var stream = PlusMemoryStream.GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));

        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }
}
