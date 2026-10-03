using System.Collections.Concurrent;
using System.Data;
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
        _client.GetHabbo().Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent();
        _client.GetHabbo().HabboStats = new Plus.HabboHotel.Users.HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        _client.GetHabbo().Inventory ??= new Plus.HabboHotel.Users.Inventory.InventoryComponent { Furniture = new Plus.HabboHotel.Users.Inventory.Furniture.FurnitureInventoryComponent([], []) };
        var user = new RoomUser(7, RoomId, 1, _room) { X = x, Y = y };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(user, _client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        users.TryAdd(1, user);
        return user;
    }

    private MoveObjectEvent MoveObject() => new(Proxy<IRoomManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null));

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
        var field = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, Proxy<ISettingsManager>((_, _) => setting.ToString()));
            Add(10, 1, 1, z: 2, height: 1, type: InteractionType.Stacktool);
            Assert.Equal((byte)state, _room.GetGameMap().GameMap[1, 1]);
            Assert.Equal(height, _room.GetGameMap().SqAbsoluteHeight(1, 1));
            Assert.Equal(2, _room.GetGameMap().ResolvePlacement(1, 1).PlacementZ);
        }
        finally { field.SetValue(null, previous); }
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 2)]
    public void StacktoolCompatibilityKeepsUnderlyingSupportWhenHelpersAreIgnored(int setting, int state, double height)
    {
        var field = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, Proxy<ISettingsManager>((_, _) => setting.ToString()));
            var support = Add(10, 1, 1, height: 1);
            support.Definition.Walkable = true;
            _room.GetGameMap().UpdateMapForItem(support);
            Add(11, 1, 1, z: 2, type: InteractionType.Stacktool);
            Assert.Equal((byte)state, _room.GetGameMap().GameMap[1, 1]);
            Assert.Equal(height, _room.GetGameMap().SqAbsoluteHeight(1, 1));
        }
        finally { field.SetValue(null, previous); }
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
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(12, 100));
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
        var map = new Gamemap(_room, new RoomModel("void", 0, 0, 0, 0, "0000\r0x00\r0000\r0000", false, 0, false));
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
        await MoveObject().Parse(_room, _client, ClientPacket(11, 2, 2, 0));
        // Remove the ordinary item before checking the underlying void permission.
        await new PickupObjectEvent(Proxy<IGameClientManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null), _database)
            .Parse(_client, ClientPacket(0, 12));
        Assert.False(map.ResolvePlacement(1, 1).CanStack);
        Assert.Equal(SquareState.Blocked, map.Model.SqState[1, 1]);
        Assert.Equal((byte)0, map.GameMap[1, 1]);
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
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(10, 175, true));
        Assert.Equal("175;1", tile!.LegacyDataString);
        Add(11, 2, 2, height: 2.35);
        await MoveObject().Parse(_room, _client, ClientPacket(10, 2, 2, 0));
        Assert.Equal("235;1", tile.LegacyDataString);
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(10, 300));
        Assert.Equal("300;1", tile.LegacyDataString);
        Assert.Equal(1, RoomEngineSerializers.FloorExtra(tile));
        foreach (var composer in new IServerPacket[] { new ObjectAddComposer(tile), new ObjectUpdateComposer(tile) })
        {
            var packet = Body(composer);
            packet.ReadUInt(); packet.ReadInt(); packet.ReadInt(); packet.ReadInt(); packet.ReadInt();
            Assert.Equal("3", packet.ReadString());
            packet.ReadString();
            Assert.Equal(1, packet.ReadInt());
            Assert.Equal(0, packet.ReadInt());
            Assert.Equal("300", packet.ReadString());
        }
        var loaded = ItemLoader.ReadRoomItem(Row(3, tile.LegacyDataString), RoomId, tile.Definition);
        Assert.Equal("300;1", loaded.LegacyDataString);
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(10, 325, false));
        Assert.Equal("325", tile.LegacyDataString);
        Assert.Equal(0, RoomEngineSerializers.FloorExtra(tile));
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
        foreach (var column in new[] { "id", "user_id", "x", "y", "rot", "limited_number", "limited_stack" })
            table.Columns.Add(column, typeof(int));
        table.Columns.Add("z", typeof(double));
        foreach (var column in new[] { "extra_data", "wall_pos", "username" }) table.Columns.Add(column, typeof(string));
        return table.Rows.Add(10, 7, 1, 1, 0, 0, 0, height, stored, "", "owner");
    }

    [Fact]
    public async Task WidgetRejectsNonMagicAndUnauthorizedAndClampsBothInputForms()
    {
        var ordinary = Add(10, 1, 1, height: 80);
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(10, 100));
        Assert.Equal(0, ordinary.GetZ);
        var helper = Add(11, 1, 1, type: InteractionType.WalkMagicTile);
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(11, -100));
        Assert.Equal(40, helper.GetZ);
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(11, 9000));
        Assert.Equal(40, helper.GetZ);
        _client.GetHabbo().Id = 99;
        _client.GetHabbo().Username = "visitor";
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(11, 0));
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
        using var manager = new PacketManager([new UpdateMagicTileAdjacentEvent()], NullLogger<PacketManager>.Instance);
        await manager.TryExecutePacket(_client, mapping[2687], ClientPacket(10, false));
        Assert.Equal(2.01, tile.GetZ);
        Assert.Contains(2816u, _client.Sent);
        var echo = new FlashIncomingPacket { Buffer = _client.Packets.Last(packet => packet.Header == 2816).Body };
        Assert.Equal(10u, echo.ReadUInt()); Assert.Equal(201, echo.ReadInt());
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
        var width = full.ReadInt(); var count = full.ReadInt();
        for (var index = 0; index < count; index++)
            Assert.Equal(_room.GetGameMap().PlacementHeightMap()[index % width, index / width], full.ReadShort());
        await new PickupObjectEvent(Proxy<IGameClientManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null), _database)
            .Parse(_client, ClientPacket(0, 10));
        Assert.Equal((short)0, DeltaAt(2, 2));
        Add(11, 1, 1, height: 2, stackable: false);
        Assert.Equal((short)(512 | 0x4000), DeltaAt(1, 1));
        await new PickupObjectEvent(Proxy<IGameClientManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null), _database)
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
        await new UpdateMagicTileEvent().Parse(_client, ClientPacket(11, 225));
        Assert.Equal((short)576, DeltaAt(2, 2));
        await new PickupObjectEvent(Proxy<IGameClientManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null), _database)
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
        var status = Body(new UserUpdateComposer([user]));
        Assert.Equal(1, status.ReadInt());
        status.ReadInt(); status.ReadInt(); status.ReadInt(); status.ReadString(); status.ReadInt(); status.ReadInt();
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
        var writes = new List<string>();
        var parameters = new Dictionary<string, object?>();
        var query = Proxy<Plus.Database.Interfaces.IQueryAdapter>((method, args) =>
        {
            if (method == "SetQuery" || method == "RunQuery" && args.Length > 0) writes.Add((string)args[0]!);
            if (method == "AddParameter") parameters[(string)args[0]!] = args[1];
            return method == "GetTable" ? row.Table : null;
        });
        _databaseField.SetValue(null, Proxy<Plus.Database.IDatabase>((method, _) => method == "GetQueryReactor" ? query : null));
        var definitions = Proxy<IItemDataManager>((method, _) => method == "get_Items" ? new Dictionary<uint, ItemDefinition> { [10] = definition } : null);
        _gameField.SetValue(null, Proxy<Plus.HabboHotel.IGame>((method, _) => method == "get_ItemManager" ? definitions : null));
        _room.GetRoomItemHandler().LoadFurniture();
        var tile = _room.GetRoomItemHandler().GetItem(10);
        Assert.Equal("200;1", tile.LegacyDataString);
        typeof(RoomItemHandling).GetMethod("SaveFurniture", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_room.GetRoomItemHandler(), null);
        Assert.Equal("200;1", parameters["edata10"]);
        Assert.Contains(writes, sql => sql.Contains("`z` = '2'"));
        // The persistent prefix and physical altitude survive another real room load.
        row["extra_data"] = parameters["edata10"]!;
        _room.GetRoomItemHandler().LoadFurniture();
        Assert.Equal("200;1", _room.GetRoomItemHandler().GetItem(10).LegacyDataString);
    }

    [Fact]
    public void LegacyWiredRotationUpdatesOldAndNewRectangleProjection()
    {
        Viewer();
        var table = Add(10, 1, 1, height: 2, length: 2);
        var box = new Plus.HabboHotel.Items.Wired.Boxes.Effects.MatchPositionBox(_room, Furni(12, InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition))
        {
            StringData = "0;1;0", ItemsData = "10:1,1,0,2,0"
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
        Assert.Equal(4, full.ReadInt()); Assert.Equal(16, full.ReadInt());
        for (var index = 0; index < 5; index++) full.ReadShort();
        Assert.Equal((short)320, full.ReadShort());
    }

    [Theory]
    [InlineData("official", 0.5, true)]
    [InlineData("strict", 0.5, true)]
    [InlineData("strict", 2.0, false)]
    public void WalkMagicFlanksProvideAnOpenSurfaceAtTheirOwnHeight(string rule, double flankHeight, bool expected)
    {
        var field = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, Proxy<ISettingsManager>((_, args) => (string)args[0]! == "pathfinding.corner_rule" ? rule : "1"));
            var map = new Gamemap(_room, new RoomModel("flanks", 0, 0, 0, 0, "0000\r00x0\r0000\r0000", false, 0, false));
            Set("_gamemap", map); map.GenerateMaps();
            Add(10, 1, 2, height: 5, stackable: false);
            Add(11, 1, 2, z: 0.5, type: InteractionType.WalkMagicTile);
            var voidHelper = Add(12, 2, 1, z: flankHeight, type: InteractionType.WalkMagicTile);
            var user = Viewer(1, 1);
            var from = new Plus.HabboHotel.Rooms.PathFinding.Vector2D(1, 1);
            var to = new Plus.HabboHotel.Rooms.PathFinding.Vector2D(2, 2);
            Assert.Equal(expected, map.IsValidStep(from, to, true, false, false, user));
            Assert.Equal(expected, map.IsValidStep2(user, from, to, true, false));
            Assert.Equal(flankHeight, map.SqAbsoluteHeight(2, 1));
            Assert.Equal((byte)1, map.GameMap[2, 1]);
            _room.GetRoomItemHandler().SetFloorItem(voidHelper, 3, 3, 0);
            Assert.False(map.IsValidStep(from, to, true, false, false, user));
            Assert.False(map.IsValidStep2(user, from, to, true, false));
        }
        finally { field.SetValue(null, previous); }
    }

    private short DeltaAt(int x, int y)
    {
        foreach (var sent in _client.Packets.Where(packet => packet.Header == ServerPacketHeader.HeightMapUpdateComposer).Reverse())
        {
            var packet = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
            var count = packet.ReadByte();
            for (var index = 0; index < count; index++)
            {
                var tx = packet.ReadByte(); var ty = packet.ReadByte(); var value = packet.ReadShort();
                if (tx == x && ty == y) return value;
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
