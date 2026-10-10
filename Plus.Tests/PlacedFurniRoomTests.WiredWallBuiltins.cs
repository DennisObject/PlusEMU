using System.Text.Json;
using Dapper;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(":w=3,7 l=12,16 l", 3, 7, 12, 341, 0)]
    [InlineData(":w=8,0 l=12,16 r", 8, 0, 12, 329, 1)]
    [InlineData(":w=3,7 l=12,61 l a=201", 3, 7, 12, 201, 0)]
    [InlineData(":w=8,0 l=12,57 r a=201", 8, 0, 12, 201, 1)]
    [InlineData(":w=3,7 l=12,-2435 l a=8001", 3, 7, 12, 8001, 0)]
    [InlineData(":w=3,7 l=12,126 l a=-1", 3, 7, 12, -1, 0)]
    public void WallBuiltinReadsUseNativeComponentsAndSignedWallIdentity(string location, int x, int y, int offset, int altitude, int side)
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, location);
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        var holder = WiredVariableRuntimeFrames.FurniHolder(wall);
        var expected = new Dictionary<string, long>
        {
            ["@position.x"] = x,
            ["@position.y"] = y,
            ["@wallitem_offset"] = offset,
            ["@altitude"] = altitude,
            ["@rotation"] = side,
            ["@position"] = (x << 8) | y,
            ["@occupation"] = (x << 16) | (y << 8) | side,
            ["@id"] = -301,
            ["@class_id"] = -10
        };

        using var reads = module.CaptureReads(expected.Keys.Select(WallBuiltinReference), frame);

        foreach (var (token, value) in expected) {
            Assert.Equal(value, module.Read(WallBuiltinReference(token), holder, frame)!.Value);
            Assert.Equal(value, reads.Read(WallBuiltinReference(token), holder, frame)!.Value);
        }

        Assert.False(WallBuiltinWrite(module, wall, frame, "@id", 302));
        Assert.False(WallBuiltinWrite(module, wall, frame, "@class_id", 11));
        Assert.Empty(module.DrainChanges());
    }

    [Theory]
    [InlineData(201, 61)]
    [InlineData(8000, -2435)]
    [InlineData(8001, -2435)]
    [InlineData(-1, 126)]
    public void WallBuiltinAltitudeWritesExactHundredthsOutsideActionLimits(int altitude, int pixelY)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,16 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@altitude", altitude));
        Assert.Equal($":w=3,7 l=12,{pixelY} l a={altitude}", wall.WallCoordinates);
        Assert.Equal(altitude, module.Read(WallBuiltinReference("@altitude"), WiredVariableRuntimeFrames.FurniHolder(wall), frame)!.Value);
        Assert.Single(store.Writes);
        Assert.Empty(module.DrainChanges());
        Assert.False(WallBuiltinWrite(module, wall, frame, "@altitude", altitude));
        Assert.Single(store.Writes);
        Assert.False(module.Mutate(WallBuiltinReference("@altitude"), WiredVariableRuntimeFrames.FurniHolder(wall), WiredVariableMutation.Give, 200, frame));
    }

    [Theory]
    [InlineData(0, 67)]
    [InlineData(13, 60)]
    [InlineData(31, 51)]
    [InlineData(32, 51)]
    public void WallBuiltinOffsetPreservesAnchorAltitudeAndBoundaryComponents(int offset, int pixelY)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@wallitem_offset", offset));
        Assert.Equal($":w=3,7 l={offset},{pixelY} l a=201", wall.WallCoordinates);
        Assert.Single(store.Writes);
        Assert.Empty(module.DrainChanges());
        Assert.False(WallBuiltinWrite(module, wall, frame, "@wallitem_offset", -1));
        Assert.False(WallBuiltinWrite(module, wall, frame, "@wallitem_offset", 33));
        Assert.Single(store.Writes);
        Assert.Equal($":w=3,7 l={offset},{pixelY} l a=201", wall.WallCoordinates);
    }

    [Theory]
    [InlineData(false, 13, 19, 60, 60)]
    [InlineData(true, 5, 11, 31, 31)]
    public void WallBuiltinSideWritesMirrorOffsetUsingRoomScale(bool halfScale, int leftOffset, int rightOffset, int rightPixelY, int leftPixelY)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel(halfScale: halfScale);
        var wall = WallSnapshotItem(301, $":w=3,7 l={leftOffset},{leftPixelY} l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@rotation", 1));
        Assert.Equal($":w=3,7 l={rightOffset},{rightPixelY} r a=201", wall.WallCoordinates);
        Assert.False(WallBuiltinWrite(module, wall, frame, "@rotation", 4));
        Assert.False(WallBuiltinWrite(module, wall, frame, "@rotation", 6));
        Assert.False(WallBuiltinWrite(module, wall, frame, "@rotation", 1));
        Assert.Single(store.Writes);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@rotation", 0));
        Assert.Equal($":w=3,7 l={leftOffset},{leftPixelY} l a=201", wall.WallCoordinates);
        Assert.Equal(2, store.Writes.Count);
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void WallBuiltinPackedPositionAndOccupationAreAtomicAndRejectInvalidSideBeforeMoving()
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=13,60 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        var variables = _room.GetWired().Variables;
        variables.FxFlushed();
        Assert.False(variables.FxDirty);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@position", (4 << 8) | 8));
        Assert.Equal(":w=4,8 l=13,60 l a=201", wall.WallCoordinates);
        Assert.True(variables.FxDirty);
        Assert.Single(store.Writes);
        wall.WallCoordinates = ":w=3,7 l=13,60 l a=201";
        Assert.True(WallBuiltinWrite(module, wall, frame, "@occupation", (4 << 16) | (8 << 8) | 1));
        Assert.Equal(":w=4,8 l=19,60 r a=201", wall.WallCoordinates);
        Assert.Equal(2, store.Writes.Count);
        wall.WallCoordinates = ":w=3,7 l=19,60 r a=201";
        _client.Packets.Clear();

        foreach (var invalidSide in new[] { 4, 6 }) {
            Assert.False(WallBuiltinWrite(module, wall, frame, "@occupation", (4 << 16) | (8 << 8) | invalidSide));
            Assert.Equal(":w=3,7 l=19,60 r a=201", wall.WallCoordinates);
        }

        Assert.Equal(2, store.Writes.Count);
        Assert.Empty(_client.Packets);
        Assert.Empty(module.DrainChanges());
    }

    [Theory]
    [InlineData(-1, 126)]
    [InlineData(8001, -2435)]
    public void WallBuiltinSignedAltitudeSurvivesOffsetSideAndSnapshotRoundTrip(int altitude, int pixelY)
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, $":w=3,7 l=12,{pixelY} l a={altitude}");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@wallitem_offset", 13));
        Assert.True(WallBuiltinWrite(module, wall, frame, "@rotation", 1));
        var baseline = wall.WallCoordinates;
        var action = WallSnapshotAction();
        Assert.True(WiredConfigurationSave.TrySave(action, new() { IntParams = [0, 0, 1, 1, 100], SelectedItems = [wall.Id] },
            TestWiredConfigurationStore.Instance, out var error, prepare: WiredRoomOperations.PrepareSnapshots), error);
        Assert.Equal(altitude, Assert.Single(action.Configuration.Snapshots).Wall!.NativeAltitude);
        var reloaded = JsonSerializer.Deserialize<WiredConfiguration>(JsonSerializer.Serialize(action.Configuration))!;
        action.ApplyConfiguration(reloaded);
        Assert.True(WallBuiltinWrite(module, wall, frame, "@altitude", 201));
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(baseline, wall.WallCoordinates);
        action.ApplyConfiguration(reloaded with { IntParams = [0, 0, 0, 1, 100] });
        wall.WallCoordinates = ":w=4,8 l=19,60 r a=201";
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.EndsWith($"r a={altitude}", wall.WallCoordinates);
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void WallBuiltinInvalidAndStaleFramesCannotPersistPublishOrIntercept()
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,16 l");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var directory = new WallBuiltinDirectory(_room);
        directory.Definitions[900] = new(900, RoomId, 7, "readonly", WiredVariableTarget.Furni,
            WiredVariableAvailability.RoomActive, true, Link: new(RoomId, WallBuiltinReference("@altitude"), true));
        var module = WallBuiltinModule(directory);
        var frame = WallBuiltinFrame(wall);
        var holder = WiredVariableRuntimeFrames.FurniHolder(wall);
        _client.Packets.Clear();
        Assert.False(module.Mutate(new(WiredVariableTarget.Furni, "custom:900"), holder, WiredVariableMutation.Set, 201, frame));
        Assert.False(module.Mutate(WallBuiltinReference("@altitude"), holder, WiredVariableMutation.Set, 201, new(RoomId + 1, [holder])));
        Assert.False(module.Mutate(WallBuiltinReference("@altitude"), holder, WiredVariableMutation.Set, (long)int.MaxValue + 1, frame));
        store.Fail = true;
        Assert.Throws<InvalidOperationException>(() => WallBuiltinWrite(module, wall, frame, "@altitude", 201));
        Assert.Equal(":w=3,7 l=12,16 l", wall.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.Empty(_client.Packets);
        Assert.Empty(module.DrainChanges());
        store.Fail = false;
        var replacement = WallSnapshotItem(wall.Id, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([replacement]);
        _client.Packets.Clear();
        Assert.Null(module.Read(WallBuiltinReference("@altitude"), holder, frame));
        Assert.False(WallBuiltinWrite(module, wall, frame, "@altitude", 202));
        Assert.Empty(store.Writes);
        Assert.Empty(_client.Packets);
        Assert.Equal(":w=3,7 l=12,61 l a=201", replacement.WallCoordinates);
        var fresh = WallBuiltinFrame(replacement);
        Assert.True(WallBuiltinWrite(module, replacement, fresh, "@altitude", 202));
        Assert.Single(store.Writes);
        Assert.Empty(module.DrainChanges());
        replacement.RoomId = RoomId + 1;
        Assert.Null(module.Read(WallBuiltinReference("@altitude"), holder, fresh));
        Assert.Null(module.Read(WallBuiltinReference("@id"), holder, fresh));
        Assert.False(WallBuiltinWrite(module, replacement, fresh, "@altitude", 203));
        Assert.Single(store.Writes);
    }

    [WiredChestDatabaseFact]
    public void WallBuiltinDatabaseRollbackAndReloadPreserveExactSignedAltitudeAndNoEvents()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("ALTER TABLE items ADD COLUMN wall_pos TEXT; INSERT INTO items(id,user_id,room_id,base_item,extra_data,wall_pos) VALUES(301,7,42,0,'',':w=3,7 l=12,61 l a=200')");
        Set("_roomItemHandling", new RoomItemHandling(_room, new RoomItemStore(fixture.Database), TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=200");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        frame.RuntimeContext!.Policy.Addons.DisableAnimation = false;
        _client.Packets.Clear();
        fixture.Connection.Execute("CREATE TRIGGER reject_wall_builtin BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reject wall builtin'");
        Assert.Throws<MySqlConnector.MySqlException>(() => WallBuiltinWrite(module, wall, frame, "@altitude", 201));
        Assert.Equal(":w=3,7 l=12,61 l a=200", wall.WallCoordinates);
        Assert.Equal(wall.WallCoordinates, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Empty(_client.Packets);
        Assert.Empty(module.DrainChanges());
        fixture.Connection.Execute("DROP TRIGGER reject_wall_builtin");
        Assert.True(WallBuiltinWrite(module, wall, frame, "@altitude", 201));
        Assert.Equal(":w=3,7 l=12,61 l a=201", fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Equal(ServerPacketHeader.WiredMovementsComposer, Assert.Single(_client.Packets).Header);

        foreach (var altitude in new[] { -1, 8001 }) {
            Assert.True(WallBuiltinWrite(module, wall, frame, "@altitude", altitude));
            var saved = fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301");
            var reloaded = WallSnapshotItem(301, saved);
            _room.GetRoomItemHandler().LoadFurniture([reloaded]);
            wall = reloaded;
            frame = WallBuiltinFrame(wall);
            Assert.Equal(altitude, module.Read(WallBuiltinReference("@altitude"), WiredVariableRuntimeFrames.FurniHolder(wall), frame)!.Value);
            Assert.True(WallBuiltinWrite(module, wall, frame, "@wallitem_offset", 13));
            Assert.True(WallBuiltinWrite(module, wall, frame, "@rotation", 1));
            Assert.Contains($"r a={altitude}", fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
            Assert.Empty(module.DrainChanges());
            Assert.True(WallBuiltinWrite(module, wall, frame, "@wallitem_offset", 12));
            Assert.True(WallBuiltinWrite(module, wall, frame, "@rotation", 0));
        }

        fixture.Connection.Execute("UPDATE items SET room_id=43 WHERE id=301");
        var before = wall.WallCoordinates;
        Assert.Throws<InvalidOperationException>(() => WallBuiltinWrite(module, wall, frame, "@altitude", 201));
        Assert.Equal(before, wall.WallCoordinates);
        Assert.Equal(before, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void WallBuiltinCorrectionLeavesFloorReadAndChangeNotificationSemanticsUntouched()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var floor = Furni(302, InteractionType.None, WiredBoxType.None);
        floor.RoomId = RoomId;
        floor.SetState(2, 3, 0, []);
        _room.GetRoomItemHandler().LoadFurniture([floor]);
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(floor);
        var holder = WiredVariableRuntimeFrames.FurniHolder(floor);
        Assert.Equal(302, module.Read(WallBuiltinReference("@id"), holder, frame)!.Value);
        Assert.Equal(10, module.Read(WallBuiltinReference("@class_id"), holder, frame)!.Value);
        Assert.Equal(0, module.Read(WallBuiltinReference("@rotation"), holder, frame)!.Value);
        Assert.True(WallBuiltinWrite(module, floor, frame, "@rotation", 2));
        Assert.Equal(2, floor.Rotation);
        Assert.Single(module.DrainChanges());
    }

    [Theory]
    [InlineData(false, false, 500, ServerPacketHeader.WiredMovementsComposer)]
    [InlineData(false, false, 750, ServerPacketHeader.WiredMovementsComposer)]
    [InlineData(true, false, 750, ServerPacketHeader.ItemUpdateComposer)]
    [InlineData(false, true, 750, ServerPacketHeader.ItemUpdateComposer)]
    public void WallBuiltinPublicationUsesSharedAnimationPolicyWithoutInterception(bool disabled, bool sideChange, int duration, uint header)
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=200");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        frame.RuntimeContext!.Policy.Addons.DisableAnimation = disabled;
        frame.RuntimeContext.Policy.Addons.AnimationTimeMs = duration;
        _client.Packets.Clear();
        Assert.True(WallBuiltinWrite(module, wall, frame, sideChange ? "@rotation" : "@altitude", sideChange ? 1 : 201));
        var packet = Assert.Single(_client.Packets);
        Assert.Equal(header, packet.Header);

        if (!disabled && !sideChange) {
            Assert.Equal(duration, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(packet.Body.AsSpan(45)));
        }

        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void WallBuiltinSignedDomainDoesNotRemoveTheAltitudeActionsOwnClamp()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,-2435 l a=8001");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var action = WallGeometryAction("wf_act_set_altitude", new() { IntParams = [0, 100], Text = "0.01", SelectedItems = [wall.Id] });
        Assert.True(action.Execute(WallSnapshotContext()));
        Assert.Equal(":w=3,7 l=12,-2435 l a=8000", wall.WallCoordinates);
    }

    [Fact]
    public void WallBuiltinUnsafeProjectionCannotPersistOrPublish()
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel(wallHeight: int.MaxValue);
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var module = WallBuiltinModule();
        var frame = WallBuiltinFrame(wall);
        _client.Packets.Clear();
        Assert.False(WallBuiltinWrite(module, wall, frame, "@altitude", 202));
        Assert.Equal(":w=3,7 l=12,61 l a=201", wall.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.Empty(_client.Packets);
        Assert.Empty(module.DrainChanges());
    }

    private static WiredVariableReference WallBuiltinReference(string token) => new(WiredVariableTarget.Furni, "internal:" + token);

    private WiredVariableFrame WallBuiltinFrame(params Item[] items)
    {
        var context = WallSnapshotContext();
        context.Triggering.FurniIds.UnionWith(items.Select(item => item.Id));

        return WiredVariableRuntimeFrames.Create(context);
    }

    private WiredVariableModule WallBuiltinModule(WallBuiltinDirectory? directory = null)
    {
        var wired = _room.GetWired();

        return new(RoomId, directory ?? new(_room), new MemoryWiredVariableStore(), TimeProvider.System,
            new RoomWiredBuiltinVariables(_room, wired.ReadBuiltin, wired.WriteBuiltin));
    }

    private static bool WallBuiltinWrite(WiredVariableModule module, Item item, WiredVariableFrame frame, string token, long value) =>
        module.Mutate(WallBuiltinReference(token), WiredVariableRuntimeFrames.FurniHolder(item), WiredVariableMutation.Set, value, frame);

    private void WallBuiltinObserver()
    {
        var actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[actor.VirtualId] = actor;
    }

    private sealed class WallBuiltinDirectory(Room room) : IWiredVariableDirectory
    {
        public Dictionary<uint, WiredVariableDefinition> Definitions { get; } = [];
        public WiredVariableDefinition? Find(uint itemId) => Definitions.GetValueOrDefault(itemId);
        public uint? GetRoomOwner(uint roomId) => room.Id == roomId ? (uint)room.OwnerId : null;
    }
}
