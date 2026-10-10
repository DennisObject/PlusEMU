using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(":w=3,7 l=13,64 r a=-1", -1, 1)]
    [InlineData(":w=3,7 l=12,16 l", 341, 0)]
    public async Task WallInspectionReadHandlerReturnsNineAuthoritativeScalarsWithoutEffects(string raw, int altitude, int side)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, raw);
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var frames = new List<WiredVariableFrame>();
        var module = InspectionReadModule((_, _, frame) => frames.Add(frame));
        _room.GetWired().Variables.FxFlushed();
        _client.Packets.Clear();
        var result = await InspectionRead(wall.Id);
        Assert.Equal(0, result.Status);
        Assert.Equal(9, result.Values.Count);
        Assert.Equal(-301, result.Values["@id"]);
        Assert.Equal(-10, result.Values["@class_id"]);
        Assert.Equal(3, result.Values["@position.x"]);
        Assert.Equal(7, result.Values["@position.y"]);
        Assert.Equal((3 << 8) | 7, result.Values["@position"]);
        Assert.Equal((3 << 16) | (7 << 8) | side, result.Values["@occupation"]);
        Assert.Equal(side, result.Values["@rotation"]);
        Assert.Equal(altitude, result.Values["@altitude"]);
        Assert.Equal(raw.Contains("13,", StringComparison.Ordinal) ? 13 : 12, result.Values["@wallitem_offset"]);
        Assert.All(frames, frame => Assert.Single(frame.Holders));
        Assert.Equal(raw, wall.WallCoordinates);
        Assert.Empty(store.Writes);
        Assert.Empty(module.DrainChanges());
        Assert.False(_room.GetWired().Variables.FxDirty);
        Assert.Single(_client.Packets);
    }

    [Fact]
    public async Task WallInspectionReadKeepsOnePlacementEvenWhenItMovesBetweenTokenReads()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        InspectionReadModule((token, _, _) =>
        {
            if (token == "@position.y") {
                wall.WallCoordinates = ":w=4,8 l=19,40 r a=-1";
            }
        });
        var result = await InspectionRead(wall.Id);
        Assert.Equal(0, result.Status);
        Assert.Equal(3, result.Values["@position.x"]);
        Assert.Equal(7, result.Values["@position.y"]);
        Assert.Equal(201, result.Values["@altitude"]);
        Assert.Equal(12, result.Values["@wallitem_offset"]);
        Assert.Equal(0, result.Values["@rotation"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WallInspectionReadCannotUpgradeInterveningDefinitionOrModelRejectionAfterAba(bool model)
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var definition = wall.Definition;
        var original = _room.GetGameMap().StaticModel;
        var alternate = WallSnapshotItem(302, wall.WallCoordinates).Definition;
        InspectionReadModule((token, after, _) =>
        {
            if (token != "@position.x") {
                return;
            }

            if (model) {
                typeof(Gamemap).GetProperty(nameof(Gamemap.StaticModel))!.SetValue(_room.GetGameMap(), after ? original : new RoomModel("0", 0, 0, 0, 0, "00\r00", 0, 0, false));
            }
            else {
                wall.Definition = after ? definition : alternate;
            }
        });
        var result = await InspectionRead(wall.Id);
        Assert.Equal(2, result.Status);
        Assert.Empty(result.Values);
        Assert.Same(definition, wall.Definition);
        Assert.Same(original, _room.GetGameMap().StaticModel);
        InspectionReadModule();
        Assert.Equal(9, (await InspectionRead(wall.Id)).Values.Count);
    }

    [Fact]
    public async Task WallInspectionReadAllowsInspectWithoutModifyAndDeniesWrongRoomWithoutReading()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var reads = 0;
        InspectionReadModule((_, after, _) => reads += after ? 1 : 0);
        _client.GetHabbo().Username = "visitor";
        Assert.Equal(1, (await InspectionRead(wall.Id)).Status);
        Assert.Equal(0, reads);
        _client.GetHabbo().Username = "owner";
        Assert.True(_room.GetWired().Settings.TrySave(_client, 1, 0, "", out _));
        _client.GetHabbo().Username = "visitor";
        Assert.False(_room.GetWired().Settings.CanModify(_client));
        Assert.Equal(0, (await InspectionRead(wall.Id)).Status);
        Assert.Equal(9, reads);
        Assert.Equal(1, (await InspectionRead(wall.Id, 99)).Status);
        Assert.Equal(9, reads);
    }

    [Fact]
    public async Task WallInspectionReadRejectsMissingMalformedOrFloorTargetsAndThrowingReadsWithoutPartialValues()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var floor = Furni(302, InteractionType.None, Plus.HabboHotel.Items.Wired.WiredBoxType.None);
        floor.RoomId = RoomId;
        var malformed = WallSnapshotItem(303, "not a wall placement");
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([floor, malformed, wall]);
        malformed.WallCoordinates = "not a wall placement"; // Loading validates legacy positions; corrupt only after attachment.
        var reads = 0;
        InspectionReadModule((_, _, _) => reads++);

        foreach (var id in new uint[] { 999, floor.Id, malformed.Id }) {
            var result = await InspectionRead(id);
            Assert.Equal(2, result.Status);
            Assert.Empty(result.Values);
        }

        Assert.Equal(0, reads);
        InspectionReadModule((token, _, _) =>
        {
            if (token == "@position.x") {
                throw new InvalidOperationException("Injected read failure after identity values.");
            }
        });
        var failed = await InspectionRead(wall.Id);
        Assert.Equal(2, failed.Status);
        Assert.Empty(failed.Values);
        InspectionReadModule();
        Assert.Equal(9, (await InspectionRead(wall.Id)).Values.Count);
    }

    [Fact]
    public async Task WallInspectionReadRejectsCapturedReplacementAndRetainsEmptyEventPayloadsAndFloorUniverse()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        var replacement = WallSnapshotItem(301, ":w=4,8 l=13,60 r a=-1");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WiredVariableFrame? captured = null;
        InspectionReadModule((token, after, frame) =>
        {
            captured ??= frame;

            if (token == "@position.x" && !after) {
                _room.GetRoomItemHandler().LoadFurniture([replacement]);
            }
        });
        var rejected = await InspectionRead(wall.Id);
        Assert.Equal(2, rejected.Status);
        Assert.Empty(rejected.Values);
        Assert.Same(wall, captured!.RuntimeContext!.FurniIdentity[wall.Id]);
        Assert.Null(captured.RuntimeContext.Event.Actor);
        Assert.Null(captured.RuntimeContext.Event.EventItem);
        Assert.Empty(captured.Trigger);
        Assert.Empty(captured.Signal);
        Assert.Empty(captured.Selector);
        Assert.Empty(captured.RuntimeContext.Targets.ResolveFurni(captured.RuntimeContext, [], Plus.HabboHotel.Items.Wired.Configuration.WiredSources.AllRoom));
        InspectionReadModule();
        var fresh = await InspectionRead(replacement.Id);
        Assert.Equal(-1, fresh.Values["@altitude"]);
        Assert.Equal(4, fresh.Values["@position.x"]);
    }

    [Fact]
    public async Task WallInspectionReadPreservesOrdinaryValuesAndCannotReuseItsFrozenInputsInAnActionFrame()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        WiredVariableFrame? captured = null;
        var module = InspectionReadModule((_, _, frame) => captured ??= frame);
        var result = await InspectionRead(wall.Id);
        var holder = WiredVariableRuntimeFrames.FurniHolder(wall);
        var ordinary = WallBuiltinFrame(wall);

        foreach (var (token, value) in result.Values) {
            Assert.Equal(value, module.Read(WallBuiltinReference(token), holder, ordinary)!.Value);
        }

        var wrongContext = new WiredVariableFrame(RoomId, [holder])
        {
            RuntimeContext = WallSnapshotContext(),
            WallInspectionSnapshot = captured!.WallInspectionSnapshot
        };
        Assert.Null(module.Read(WallBuiltinReference("@position.x"), holder, wrongContext));
        Assert.Null(_room.GetWired().ReadBuiltin(WallBuiltinReference("@altitude"), holder, wrongContext));
        wall.WallCoordinates = "not a placement";
        Assert.Equal(-301, module.Read(WallBuiltinReference("@id"), holder, ordinary)!.Value);
        Assert.Equal(-10, module.Read(WallBuiltinReference("@class_id"), holder, ordinary)!.Value);
        Assert.Null(module.Read(WallBuiltinReference("@altitude"), holder, ordinary));
    }

    [Fact]
    public async Task WallInspectionReadCarriesEngineTimeWithoutActorSourceOrDispatch()
    {
        WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var engine = typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetWired())!;
        engine.GetType().GetField("_now", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, (Func<long>)(() => 1500));
        InspectionReadModule((_, _, frame) =>
        {
            Assert.Equal(1500, frame.RuntimeContext!.NowMilliseconds);
            Assert.Null(frame.RuntimeContext.Event.Actor);
            Assert.Null(frame.RuntimeContext.Event.EventItem);
            Assert.Null(frame.RuntimeContext.Trigger);
            Assert.Empty(frame.RuntimeContext.Triggering.FurniIds);
            Assert.Empty(frame.RuntimeContext.Triggering.UserIds);
        });
        Assert.Equal(9, (await InspectionRead(wall.Id)).Values.Count);
    }

    [WiredChestDatabaseFact]
    public async Task WallInspectionReadReflectsDurableUpdateReloadAndRollbackWithoutAcknowledgingWrites()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("ALTER TABLE items ADD COLUMN wall_pos TEXT; INSERT INTO items(id,user_id,room_id,base_item,extra_data,wall_pos) VALUES(301,7,42,0,'',':w=3,7 l=12,61 l a=201')");
        Set("_roomItemHandling", new RoomItemHandling(_room, new RoomItemStore(fixture.Database), TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=12,61 l a=201");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var module = InspectionInstallModule();
        await InspectionWrite(wall.Id, "@wallitem_offset", 13);
        await InspectionWrite(wall.Id, "@rotation", 1);
        await InspectionWrite(wall.Id, "@altitude", -1);
        var saved = fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301");
        var reloaded = WallSnapshotItem(301, saved);
        _room.GetRoomItemHandler().LoadFurniture([reloaded]);
        var result = await InspectionRead(reloaded.Id);
        Assert.Equal(-1, result.Values["@altitude"]);
        Assert.Equal(19, result.Values["@wallitem_offset"]);
        Assert.Equal(1, result.Values["@rotation"]);
        fixture.Connection.Execute("CREATE TRIGGER reject_inspection_wall BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reject inspection wall'");
        await Assert.ThrowsAsync<MySqlConnector.MySqlException>(() => InspectionWrite(reloaded.Id, "@altitude", 8001));
        Assert.Equal(saved, reloaded.WallCoordinates);
        Assert.Equal(saved, fixture.Connection.QuerySingle<string>("SELECT wall_pos FROM items WHERE id=301"));
        Assert.Equal(-1, (await InspectionRead(reloaded.Id)).Values["@altitude"]);
        Assert.Single(_client.Packets);
        Assert.Empty(module.DrainChanges());
    }

    [Theory]
    [InlineData("_roomUserManager", false, false)]
    [InlineData("_roomItemHandling", false, false)]
    [InlineData("_roomItemHandling", true, false)]
    [InlineData("_roomItemHandling", true, true)]
    public async Task WallInspectionReadHandlerFailsClosedWhenCaptureOrFinalMembershipIsDisposed(string dependency, bool final, bool leaveRoom)
    {
        var store = WallSnapshotInstallStore();
        WallGeometryModel();
        var wall = WallSnapshotItem(301, ":w=3,7 l=13,64 r a=-1");
        _room.GetRoomItemHandler().LoadFurniture([wall]);
        var field = typeof(Room).GetField(dependency, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = field.GetValue(_room);
        var reads = 0;
        var module = InspectionReadModule((token, after, _) =>
        {
            if (!after) {
                return;
            }

            reads++;

            if (final && token == "@wallitem_offset") {
                field.SetValue(_room, null);

                if (leaveRoom) {
                    _client.GetHabbo().CurrentRoom = null;
                }
            }
        });
        _room.GetWired().Variables.FxFlushed();

        try {
            if (!final) {
                field.SetValue(_room, null);
            }

            var result = await InspectionRead(wall.Id);
            Assert.Equal(leaveRoom ? 1 : 2, result.Status);
            Assert.Empty(result.Values);
            Assert.Equal(final ? 9 : 0, reads);
            Assert.Single(_client.Packets);
            Assert.Empty(store.Writes);
            Assert.Empty(module.DrainChanges());
            Assert.False(_room.GetWired().Variables.FxDirty);
            Assert.Equal(":w=3,7 l=13,64 r a=-1", wall.WallCoordinates);
        }
        finally {
            field.SetValue(_room, original);
            _client.GetHabbo().CurrentRoom = _room;
        }
    }

    private WiredVariableModule InspectionReadModule(Action<string, bool, WiredVariableFrame>? observe = null)
    {
        var wired = _room.GetWired();
        var inner = new RoomWiredBuiltinVariables(_room, wired.ReadBuiltin, wired.WriteBuiltin);
        var module = new WiredVariableModule(RoomId, new WallBuiltinDirectory(_room), new MemoryWiredVariableStore(), _interactionClock,
            new InspectionReadBuiltins(inner, observe));
        typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wired.Variables, module);

        return module;
    }

    private async Task<(int Status, Dictionary<string, long> Values)> InspectionRead(uint itemId, uint? requestedRoom = null)
    {
        _client.Packets.Clear();
        await new WiredVariableInspectionRequestEvent(new WiredVariableInspectionService(NullLogger<WiredVariableInspectionService>.Instance))
            .Parse(_room, _client, ClientPacket(1, 77, unchecked((int)(requestedRoom ?? RoomId)), 1, unchecked((int)itemId), 1));
        var sent = Assert.Single(_client.Packets);
        Assert.Equal(9483u, sent.Header);
        var packet = new FlashIncomingPacket { Buffer = sent.Body };
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(77, packet.ReadInt());
        Assert.Equal(requestedRoom ?? RoomId, packet.ReadUInt());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(itemId, packet.ReadUInt());
        Assert.Equal(1, packet.ReadInt());
        var status = packet.ReadInt();
        var count = packet.ReadInt();
        var values = new Dictionary<string, long>();

        for (var index = 0; index < count; index++) {
            var token = packet.ReadString();
            Assert.True(packet.ReadBool());
            values.Add(token, ((long)packet.ReadInt() << 32) | (uint)packet.ReadInt());
        }

        Assert.False(packet.HasDataRemaining());

        return (status, values);
    }

    private sealed class InspectionReadBuiltins(IWiredBuiltinVariables inner, Action<string, bool, WiredVariableFrame>? observe) : IWiredBuiltinVariables
    {
        public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
        {
            var token = RoomWiredBuiltinVariables.Normalize(reference.Token);
            observe?.Invoke(token, false, frame);
            var result = inner.Read(reference, holder, frame);
            observe?.Invoke(token, true, frame);

            return result;
        }
        public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame) =>
            throw new InvalidOperationException("Inspection must never write.");
    }
}
