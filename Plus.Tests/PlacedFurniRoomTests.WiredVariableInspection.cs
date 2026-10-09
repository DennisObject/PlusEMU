using System.Reflection;
using Dapper;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task VariableInspectionHandlerCapturesNonzeroEngineClockForProjectileReads()
    {
        WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, ":w=1,1 l=12,20 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var floor = Furni(302, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, floor, 3, 2, 0, true, false, false));
        var engine = typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetWired())!;
        long now = 1000;
        engine.GetType().GetField("_now", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, (Func<long>)(() => now));
        Assert.True(WiredProjectileFlights.For(_room).Begin(floor, 1, 2, 0, 1000, now));
        now = 1500;
        WiredVariableFrame? captured = null;
        var module = InspectionInstallModule(onRead: frame => captured ??= frame);
        await InspectionWrite(wall.Id, "@altitude", 202);
        Assert.NotNull(captured!.RuntimeContext);
        Assert.Equal(1500, captured.RuntimeContext.NowMilliseconds);
        Assert.Equal(2, module.Read(WallBuiltinReference("@projectile.animation.position.x"), WiredVariableRuntimeFrames.FurniHolder(floor), captured)!.Value);
    }

    [Fact]
    public void VariableInspectionCaptureSharesReferencesAndNamesWithoutInventingEventSources()
    {
        WallSnapshotInstallStore();
        var wall = WallSnapshotItem(301, ":w=1,1 l=12,20 l a=201");
        wall.Definition.PublicName = "captured wall";
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var menu = new WiredVariableMenu(_room, _room.GetWired().Variables);
        var (frame, names) = ((WiredVariableFrame, IReadOnlyDictionary<long, string>))typeof(WiredVariableMenu)
            .GetMethod("LiveHolders", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, [WiredVariableTarget.Furni])!;
        var context = frame.RuntimeContext!;
        Assert.Equal(WiredEventKind.Inspection, context.Event.Kind);
        Assert.Null(context.Event.Actor);
        Assert.Null(context.Event.EventItem);
        Assert.Null(context.Trigger);
        Assert.Empty(context.Triggering.FurniIds);
        Assert.Empty(context.Triggering.UserIds);
        Assert.Empty(frame.Trigger);
        Assert.Empty(frame.Signal);
        Assert.Empty(context.Targets.ResolveFurni(context, [], WiredSources.AllRoom));
        Assert.Contains(WiredVariableRuntimeFrames.FurniHolder(wall), frame.Holders);
        Assert.Same(wall, context.FurniIdentity[wall.Id]);
        Assert.Equal("captured wall", names[wall.Id]);
        Assert.DoesNotContain(WiredEventKind.Inspection, WiredTriggerConfiguration.Events.Values);
        var replacement = WallSnapshotItem(301, ":w=1,1 l=13,20 l a=201");
        replacement.Definition.PublicName = "replacement wall";
        _room.GetRoomItemHandler().LoadFurniture([replacement]);
        Assert.Equal("captured wall", names[wall.Id]);
        Assert.Same(wall, context.FurniIdentity[wall.Id]);
        Assert.Null(_room.GetWired().ReadBuiltin(WallBuiltinReference("@wallitem_offset"), WiredVariableRuntimeFrames.FurniHolder(wall), frame));
    }

    [Theory]
    [InlineData("@wallitem_offset", 13, ":w=3,7 l=13,60 l a=201")]
    [InlineData("@rotation", 1, ":w=3,7 l=20,61 r a=201")]
    [InlineData("@altitude", -1, ":w=3,7 l=12,126 l a=-1")]
    [InlineData("@altitude", 8001, ":w=3,7 l=12,-2435 l a=8001")]
    public async Task VariableInspectionHandlerWritesOwnedWallAndRefreshesExactSnapshot(string token, int value, string expected)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var module = InspectionInstallModule();
        _room.GetWired().Variables.FxFlushed();
        _client.Packets.Clear();
        await InspectionWrite(wall.Id, token, value);
        Assert.Equal(expected, wall.WallCoordinates);
        Assert.Equal((wall.Id, expected), Assert.Single(store.Writes));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredMovementsComposer || packet.Header == ServerPacketHeader.ItemUpdateComposer);
        Assert.Contains(_client.Packets, packet => packet.Header == 9480);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
        Assert.Empty(module.DrainChanges());
        Assert.True(_room.GetWired().Variables.FxDirty);
    }

    [Fact]
    public async Task VariableInspectionHandlerPreservesDeniedMalformedAndInvalidPackedRefusals()
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        const string baseline = ":w=3,7 l=19,60 r a=201";
        var wall = WallSnapshotItem(301, baseline);
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = InspectionInstallModule();
        _client.GetHabbo().Username = "visitor";
        _client.Packets.Clear();
        await InspectionWrite(wall.Id, "@wallitem_offset", 13);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
        Assert.Equal(baseline, wall.WallCoordinates);
        Assert.Empty(store.Writes);
        _client.GetHabbo().Username = "owner";
        _client.Packets.Clear();
        await new WiredUserVariableUpdate64Event(new WiredVariableMenuService()).Parse(_room, _client,
            ClientPacket(1, 1, (int)wall.Id, 0, 0, 13, "internal:@wallitem_offset", 99));
        Assert.Empty(_client.Packets);

        foreach (var side in new[] { 4, 6 }) {
            _client.Packets.Clear();
            await InspectionWrite(wall.Id, "@occupation", (4 << 16) | (8 << 8) | side);
            Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
            Assert.Contains(_client.Packets, packet => packet.Header == 9480);
            Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredMovementsComposer || packet.Header == ServerPacketHeader.ItemUpdateComposer);
            Assert.Equal(baseline, wall.WallCoordinates);
        }

        Assert.Empty(store.Writes);
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public async Task VariableInspectionHandlerWritesFloorBuiltinWithoutChangingFloorNotification()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var floor = Furni(302, InteractionType.None, WiredBoxType.None);
        floor.RoomId = RoomId;
        floor.SetState(2, 3, 0, []);
        _room.GetRoomItemHandler().LoadFurniture([floor]);
        var module = InspectionInstallModule();
        _client.Packets.Clear();
        await InspectionWrite(floor.Id, "@rotation", 2);
        Assert.Equal(2, floor.Rotation);
        Assert.Contains(_client.Packets, packet => packet.Header == 9480);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
        var change = Assert.Single(module.DrainChanges());
        Assert.Equal(2, change.Origin);
        Assert.Equal("@rotation", change.InternalKey);
    }

    [Fact]
    public async Task VariableInspectionHandlerKeepsExactCustomWritesAndUnchangedAdmission()
    {
        WallSnapshotInstallStore();
        var floor = Furni(302, InteractionType.None, WiredBoxType.None);
        floor.RoomId = RoomId;
        _room.GetRoomItemHandler().LoadFurniture([floor]);
        var directory = new WallBuiltinDirectory(_room);
        directory.Definitions[900] = new(900, RoomId, 7, "custom", WiredVariableTarget.Furni,
            WiredVariableAvailability.RoomActive, true);
        var module = InspectionInstallModule(directory);
        var holder = WiredVariableRuntimeFrames.FurniHolder(floor);
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "custom:900");
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Give, 10, new(RoomId, [holder])));
        module.DrainChanges();
        const long exact = 9007199254740993L;

        for (var repeat = 0; repeat < 2; repeat++) {
            _client.Packets.Clear();
            await new WiredUserVariableUpdate64Event(new WiredVariableMenuService()).Parse(_room, _client,
                ClientPacket(1, 1, (int)floor.Id, 900, unchecked((int)(exact >> 32)), unchecked((int)exact)));
            Assert.Equal(exact, module.Read(reference, holder, new(RoomId, [holder]))!.Value);
            Assert.Contains(_client.Packets, packet => packet.Header == 9480);
            Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
            Assert.Equal(2, Assert.Single(module.DrainChanges()).Origin);
        }
    }

    [Fact]
    public async Task VariableInspectionHandlerRejectsSameIdReplacementAfterCaptureAndFreshRequestSucceeds()
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        var replacement = WallSnapshotItem(301, ":w=3,7 l=13,60 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var replace = true;
        WiredVariableFrame? captured = null;
        var module = InspectionInstallModule(onRead: frame =>
        {
            captured ??= frame;

            if (replace) {
                replace = false;
                _room.GetRoomItemHandler().LoadFurniture([replacement]);
            }
        });
        _client.Packets.Clear();
        await InspectionWrite(wall.Id, "@altitude", 202);
        Assert.Equal(":w=3,7 l=13,60 l a=201", replacement.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
        Assert.NotNull(captured!.RuntimeContext);
        Assert.Empty(module.DrainChanges());
        _client.Packets.Clear();
        await InspectionWrite(replacement.Id, "@altitude", 202);
        Assert.EndsWith("a=202", replacement.WallCoordinates);
        Assert.Single(store.Writes);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
        Assert.Empty(module.DrainChanges());
    }

    [WiredChestDatabaseFact]
    public async Task VariableInspectionHandlerSqlFailureDoesNotPublishAndSuccessfulWriteReloadsExactWall()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("ALTER TABLE items ADD COLUMN wall_pos TEXT; INSERT INTO items(id,user_id,room_id,base_item,extra_data,wall_pos) VALUES(301,7,42,0,'',':w=3,7 l=12,61 l a=201')");
        Set("_roomItemHandling", new RoomItemHandling(_room, new RoomItemStore(fixture.Database), TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WallBuiltinObserver();
        var module = InspectionInstallModule();
        _client.Packets.Clear();
        fixture.Connection.Execute("CREATE TRIGGER reject_menu_wall BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reject menu wall'");
        await Assert.ThrowsAsync<MySqlConnector.MySqlException>(() => InspectionWrite(wall.Id, "@wallitem_offset", 13));
        Assert.Equal(":w=3,7 l=12,61 l a=201", wall.WallCoordinates);
        Assert.Equal(wall.WallCoordinates, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Empty(_client.Packets);
        Assert.Empty(module.DrainChanges());
        fixture.Connection.Execute("DROP TRIGGER reject_menu_wall");
        await InspectionWrite(wall.Id, "@wallitem_offset", 13);
        await InspectionWrite(wall.Id, "@rotation", 1);
        await InspectionWrite(wall.Id, "@altitude", -1);
        var saved = fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301");
        Assert.Equal(wall.WallCoordinates, saved);
        Assert.EndsWith("r a=-1", saved);
        var reloaded = WallSnapshotItem(wall.Id, saved);
        _room.GetRoomItemHandler().LoadFurniture([reloaded]);
        _client.Packets.Clear();
        await InspectionWrite(reloaded.Id, "@altitude", 8001);
        Assert.EndsWith("r a=8001", reloaded.WallCoordinates);
        Assert.Equal(reloaded.WallCoordinates, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.BroadcastMessageAlertComposer);
        Assert.Empty(module.DrainChanges());
    }

    private Task InspectionWrite(uint itemId, string token, int value) =>
        new WiredUserVariableUpdate64Event(new WiredVariableMenuService()).Parse(_room, _client,
            ClientPacket(1, 1, unchecked((int)itemId), 0, value < 0 ? -1 : 0, value, "internal:" + token));

    private WiredVariableModule InspectionInstallModule(WallBuiltinDirectory? directory = null, Action<WiredVariableFrame>? onRead = null)
    {
        var wired = _room.GetWired();
        var variables = wired.Variables;
        var module = new WiredVariableModule(RoomId, directory ?? new(_room), new MemoryWiredVariableStore(), _interactionClock,
            new RoomWiredBuiltinVariables(_room, (reference, holder, frame) =>
            {
                var value = wired.ReadBuiltin(reference, holder, frame);
                onRead?.Invoke(frame);

                return value;
            }, wired.WriteBuiltin));
        typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(variables, module);

        return module;
    }
}
