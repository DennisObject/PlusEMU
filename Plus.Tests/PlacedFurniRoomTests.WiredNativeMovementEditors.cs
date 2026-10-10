using System.Reflection;
using Dapper;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("wf_act_move_rotate")]
    [InlineData("wf_act_move_to_dir")]
    public void NativeMovementFreshActualOpenDoesNotMintRuntimeAuthority(string name)
    {
        var (original, store) = NativeMovementFresh(name);
        var before = (original as IWiredConfiguredItem)?.Configuration;
        original.Item.Interactor.OnTrigger(_client, original.Item, 0, true);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredEffectConfigComposer);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.Same(before, (current as IWiredConfiguredItem)?.Configuration);
        Assert.Empty(store.Saves);
    }

    [Theory]
    [InlineData("wf_act_move_rotate")]
    [InlineData("wf_act_move_to_dir")]
    public async Task NativeMovementFreshActualHandlerSavesAndReopens(string name)
    {
        var (original, store) = NativeMovementFresh(name);
        await CanonicalHandler().Parse(_client, name == "wf_act_move_rotate"
            ? ClientPacket(601, 2, 5, 2, "", 0, 3, 1, 201, 0, 0, 0)
            : ClientPacket(601, 3, 2, 6, 1, "", 0, 3, 1, 201, 0, 0, 0));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(name == "wf_act_move_rotate" ? new[] { 2, 4, 201, 0 } : new[] { 2, 6, 201, 1 }, saved.IntParams);
        Assert.Equal(3, saved.Delay);
        Assert.NotNull(saved.Origin!.Native);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(current);

        if (name == "wf_act_move_to_dir") {
            Assert.Same(original, current);
        }
        else {
            Assert.NotSame(original, current);
        }

        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        _client.Packets.Clear();
        current.Item.Interactor.OnTrigger(_client, current.Item, 0, true);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredEffectConfigComposer);
        Assert.Equal(name == "wf_act_move_rotate" ? new[] { 5, 2 } : new[] { 2, 6, 1 }, WiredEditorSnapshot.Capture(configured).Native!.OwnedIntParams);
    }

    [Theory]
    [InlineData("wf_act_move_rotate")]
    [InlineData("wf_act_move_to_dir")]
    public async Task NativeMovementStoredV1ActualNoopKeepsConfigurationAndInactiveState(string name)
    {
        var runtime = new WiredConfiguration
        {
            IntParams = name == "wf_act_move_rotate" ? [2, 4, 201, 0] : [2, 6, 201, 1],
            Text = "inactive bytes",
            Delay = 3
        };
        var (box, store) = CanonicalBox(name, runtime);
        var before = box.Configuration;
        await CanonicalHandler().Parse(_client, name == "wf_act_move_rotate"
            ? ClientPacket(601, 2, 5, 2, "", 0, 3, 1, 201, 0, 0, 0)
            : ClientPacket(601, 3, 2, 6, 1, "", 0, 3, 1, 201, 0, 0, 0));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        Assert.Empty(store.Saves);
        Assert.Same(before, box.Configuration);
        Assert.Equal("inactive bytes", box.Configuration.Text);
    }

    [Fact]
    public async Task NativeMovementNonemptyConcreteRotateRefusesEditorWithoutLosingLegacyFields()
    {
        var (box, store) = NativeMovementFresh("wf_act_move_rotate");
        box.StringData = "2;1";
        box.BoolData = true;
        box.ItemsData = "legacy bytes";
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredEffectConfigComposer);
        await CanonicalHandler().Parse(_client, ClientPacket(601, 2, 5, 2, "", 0, 0, 1, 100, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.Equal("2;1", box.StringData);
        Assert.True(box.BoolData);
        Assert.Equal("legacy bytes", box.ItemsData);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(box, current);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public void NativeMovementDynamicDirectionWallsNeverPublishCollision(int source)
    {
        var (box, _) = CanonicalBox("wf_act_move_to_dir", new() { IntParams = [2, 0, source, 1] });
        var wall = WallSnapshotItem(301, ":w=1,2 l=11,53 l");
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, wall));
        var actor = new Plus.HabboHotel.Rooms.RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 1, Y = 0 };
        BuiltinUsers()[actor.VirtualId] = actor;
        _room.GetGameMap().AddUserToMap(actor, new(1, 0));
        var collisions = 0;
        typeof(Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction).GetField("_publish", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(box,
            (Action<Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeEvent>)(e =>
            {
                if (e.Kind == Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Collision) {
                    collisions++;
                }
            }));
        var context = WallSnapshotContext();

        if (source == 200) {
            context.SelectorPool.FurniIds.Add(wall.Id);
        }
        else {
            context.Signal = new(new(), new Dictionary<string, long>());
            context.Signal.Selection.FurniIds.Add(wall.Id);
        }

        Assert.False(((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction)box).Execute(context));
        Assert.Equal(0, collisions);
        Assert.Equal(":w=1,2 l=11,53 l", wall.WallCoordinates);
    }

    [Theory]
    [InlineData("wf_act_move_rotate")]
    [InlineData("wf_act_move_to_dir")]
    public async Task NativeMovementActualSaveRefusesStaticWallsWithoutPublication(string name)
    {
        var (original, store) = NativeMovementFresh(name);
        var wall = WallSnapshotItem(301, ":w=1,2 l=11,53 l");
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, wall));
        await CanonicalHandler().Parse(_client, name == "wf_act_move_rotate"
            ? ClientPacket(601, 2, 5, 2, "", 1, -301, 0, 1, 100, 0, 0, 0)
            : ClientPacket(601, 3, 2, 0, 1, "", 1, -301, 0, 1, 100, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.DoesNotContain(_client.Packets, p => p.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    [Fact]
    public void NativeMovementEveryOwnedCombinationCompilesAndReversesWithoutInlineSources()
    {
        foreach (var name in new[] { "wf_act_move_rotate", "wf_act_move_to_dir" }) {
            var (original, _) = NativeMovementFresh(name);
            var descriptor = name == "wf_act_move_rotate"
                ? RotateDescriptor() : ((IWiredConfiguredItem)original).Descriptor;

            foreach (var source in new[] { 0, 100, 200, 201 }) {
                for (var first = 0; first < (name == "wf_act_move_rotate" ? 12 : 8); first++) {
                    for (var second = 0; second < (name == "wf_act_move_rotate" ? 4 : 7); second++) {
                        for (var block = 0; block < (name == "wf_act_move_rotate" ? 1 : 2); block++) {
                            var native = new WiredNativeEditorConfiguration
                            {
                                Category = WiredBoxCategory.Action,
                                NativeCode = name == "wf_act_move_rotate" ? 4 : 13,
                                OwnedIntParams = name == "wf_act_move_rotate" ? [first, second] : [first, second, block],
                                FurniSourceTypes = [source],
                                Delay = 3
                            };
                            Assert.True(WiredNativeEditorProjection.TryCompile(601, descriptor, native, out var runtime));
                            Assert.Equal(source, runtime.IntParams[2]);
                            Assert.Equal(3, runtime.Delay);
                            var inverse = WiredNativeEditorProjection.TrustLegacy(601, descriptor, new() { IntParams = runtime.IntParams, FurniSources = runtime.FurniSources, Delay = 3 });
                            Assert.True(WiredNativeEditorProjection.TryProject(original.Item, descriptor, inverse, out var projected));
                            Assert.True(WiredNativeEditorProjection.SameBody(native, projected));
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("wf_act_move_rotate", 4, 0, 0)]
    [InlineData("wf_act_move_to_dir", 13, 0, 1)]
    public void NativeMovementActualFooterUsesSeparateSourcesAndExplicitOwnedDefaults(string name, int code, int firstDefault, int lastDefault)
    {
        var (box, _) = NativeMovementFresh(name);
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        var packet = Assert.Single(_client.Packets.Where(p => p.Header == ServerPacketHeader.WiredEffectConfigComposer));
        var input = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = packet.Body };
        input.ReadInt();
        ReadInts();
        ReadInts();
        input.ReadInt();
        input.ReadUInt();
        Assert.Equal("", input.ReadString());
        var defaults = name == "wf_act_move_rotate" ? new[] { 0, 0 } : new[] { 0, 0, 1 };
        Assert.Equal(defaults, ReadInts());
        Assert.Equal(0, input.ReadInt());
        Assert.Equal(new[] { 100 }, ReadInts());
        Assert.Empty(ReadInts());
        Assert.Equal(code, input.ReadInt());
        Assert.Equal(0, input.ReadInt());
        Assert.True(input.ReadBool());
        Assert.Equal(1, input.ReadInt());
        Assert.Equal(new[] { 0, 100, 200, 201 }, ReadInts());
        Assert.Equal(0, input.ReadInt());
        Assert.Equal(new[] { 100 }, ReadInts());
        Assert.Empty(ReadInts());
        Assert.False(input.ReadBool());
        Assert.Equal(0, input.ReadInt());
        Assert.Equal(defaults, ReadInts());
        Assert.Equal(firstDefault, defaults[0]);
        Assert.Equal(lastDefault, defaults[^1]);
        Assert.False(input.HasDataRemaining());
        int[] ReadInts() => Enumerable.Range(0, input.ReadInt()).Select(_ => input.ReadInt()).ToArray();
    }

    [Theory]
    [InlineData("rights")]
    [InlineData("configuration")]
    [InlineData("definition")]
    [InlineData("descriptor")]
    [InlineData("room")]
    [InlineData("placement")]
    [InlineData("pick-definition")]
    [InlineData("pick-placement")]
    [InlineData("pick-kind")]
    [InlineData("pick-temporary")]
    [InlineData("unregistered")]
    public void NativeMovementFreshDirectionPublisherChecksWholeRequestAfterRightsCallback(string change)
    {
        var (original, store) = NativeMovementFresh("wf_act_move_to_dir");
        var box = (IWiredConfiguredItem)original;
        var pick = CanonicalPlace(602, 1, 1);
        var request = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 13,
            OwnedIntParams = [2, 6, 1],
            FurniSourceTypes = [100],
            PrimaryItems = [new(602, false)],
            Delay = 3
        };
        var proof = _room.GetWired().CaptureFreshDirection(original, request, () => true);
        Assert.NotNull(proof);
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, request, out var runtime));
        var persisted = false;
        var allowed = true;
        bool Rights()
        {
            switch (change) {
                case "rights":
                    allowed = false;
                    break;
                case "configuration":
                    box.ApplyConfiguration(runtime);
                    break;
                case "definition":
                    original.Item.Definition = new() { ItemName = "wf_act_move_to_dir", InteractionName = "wf_act_move_to_dir" };
                    break;
                case "descriptor":
                    original.Item.Definition.InteractionName = original.Item.Definition.ItemName = "wf_act_move_rotate";
                    break;
                case "room":
                    original.Item.RoomId = RoomId + 1;
                    break;
                case "placement":
                    original.Item.SetState(2, 2, 0, []);
                    break;
                case "pick-definition":
                    pick.Definition = new() { Type = ItemType.Floor };
                    break;
                case "pick-placement":
                    pick.SetState(2, 1, 0, []);
                    break;
                case "pick-kind":
                    pick.Definition.Type = ItemType.Wall;
                    break;
                case "pick-temporary":
                    typeof(Item).GetField("<IsTemporary>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pick, true);
                    break;
                case "unregistered":
                    _room.GetWired().TryRemove(original.Item.Id);
                    break;
            }

            return allowed;
        }
        Assert.Throws<InvalidOperationException>(() => _room.GetWired().PublishFreshDirection(proof!, runtime, Rights, () => persisted = true));
        Assert.False(persisted);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public void NativeMovementDirectionGenericProjectionCannotMintFreshOrReinstallOriginalDraft()
    {
        var (original, _) = NativeMovementFresh("wf_act_move_to_dir");
        var box = (IWiredConfiguredItem)original;
        var initial = box.Configuration;
        Assert.False(WiredNativeEditorProjection.TryProject(box.Item, box.Descriptor, initial, out _));
        Assert.False(WiredNativeEditorProjection.TryProject(box.Item, box.Descriptor, new(), out _));
        Assert.NotNull(_room.GetWired().CaptureFreshDirection(original, null, () => true));
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, new()
        { Category = WiredBoxCategory.Action, NativeCode = 13, OwnedIntParams = [0, 0, 1], FurniSourceTypes = [100], Delay = 0 }, out var valid));
        box.ApplyConfiguration(valid);
        Assert.Throws<InvalidDataException>(() => box.ApplyConfiguration(initial));
        Assert.Throws<InvalidDataException>(() => box.ApplyConfiguration(new()));
        Assert.Null(_room.GetWired().CaptureFreshDirection(original, null, () => true));
    }

    [Fact]
    public void NativeMovementFreshDirectionCaptureRechecksRightsMutationAndRequestIdentity()
    {
        var (original, _) = NativeMovementFresh("wf_act_move_to_dir");
        var calls = 0;
        Assert.Null(_room.GetWired().CaptureFreshDirection(original, null, () =>
        {
            if (++calls == 2) {
                original.Item.RoomId = RoomId + 1;
            }

            return true;
        }));
    }

    [Theory]
    [InlineData("wf_act_move_rotate")]
    [InlineData("wf_act_move_to_dir")]
    public async Task NativeMovementActualHandlerRejectsOwnedCountBoundsSourceAndTrailingBytes(string name)
    {
        var (original, store) = NativeMovementFresh(name);
        var requests = name == "wf_act_move_rotate"
            ? new[] { ClientPacket(601, 3, 5, 2, 0, "", 0, 0, 1, 100, 0, 0, 0), ClientPacket(601, 2, 12, 0, "", 0, 0, 1, 100, 0, 0, 0), ClientPacket(601, 2, 5, 4, "", 0, 0, 1, 100, 0, 0, 0) }
            : new[] { ClientPacket(601, 4, 2, 6, 100, 1, "", 0, 0, 1, 100, 0, 0, 0), ClientPacket(601, 3, 8, 0, 1, "", 0, 0, 1, 100, 0, 0, 0), ClientPacket(601, 3, 2, 7, 1, "", 0, 0, 1, 100, 0, 0, 0) };

        foreach (var packet in requests) {
            await CanonicalHandler().Parse(_client, packet);
        }

        await CanonicalHandler().Parse(_client, name == "wf_act_move_rotate"
            ? ClientPacket(601, 2, 5, 2, "", 0, 0, 1, 900, 0, 0, 0)
            : ClientPacket(601, 3, 2, 6, 2, "", 0, 0, 1, 100, 0, 0, 0));
        await CanonicalHandler().Parse(_client, name == "wf_act_move_rotate"
            ? ClientPacket(601, 2, 5, 2, "", 0, 0, 1, 100, 0, 0, 0, 999)
            : ClientPacket(601, 3, 2, 6, 1, "", 0, 0, 1, 100, 0, 0, 0, 999));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.DoesNotContain(_client.Packets, p => p.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public void NativeMovementDirectionDynamicWallsAreFilteredWithoutChangingFloorMovement(int source)
    {
        var (box, _) = CanonicalBox("wf_act_move_to_dir", new() { IntParams = [2, 0, source, 1] });
        var floor = CanonicalPlace(602, 2, 2);
        var wall = WallSnapshotItem(301, ":w=1,2 l=11,53 l");
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, wall));
        var actor = new Plus.HabboHotel.Rooms.RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 1, Y = 0 };
        BuiltinUsers()[actor.VirtualId] = actor;
        _room.GetGameMap().AddUserToMap(actor, new(1, 0));
        var context = WallSnapshotContext();

        if (source == 200) {
            context.SelectorPool.FurniIds.UnionWith([floor.Id, wall.Id]);
        }
        else {
            context.Signal = new(new([floor.Id, wall.Id]), new Dictionary<string, long>());
        }

        Assert.True(((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction)box).Execute(context));
        Assert.Equal((3, 2), (floor.GetX, floor.GetY));
        Assert.Equal(":w=1,2 l=11,53 l", wall.WallCoordinates);
        var directions = (Plus.HabboHotel.Items.Wired.Modern.Actions.WiredDirectionalActions)typeof(Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction)
            .GetField("_directions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;
        var headings = (Dictionary<Item, int>)typeof(Plus.HabboHotel.Items.Wired.Modern.Actions.WiredDirectionalActions)
            .GetField("_headings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(directions)!;
        Assert.Equal(new[] { floor }, headings.Keys);
    }

    [WiredChestDatabaseFact]
    public async Task NativeMovementActualSqlRollbackKeepsFreshBoxesThenDurableSaveReloadsBothCards()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");

        foreach (var name in new[] { "wf_act_move_rotate", "wf_act_move_to_dir" }) {
            db.Connection.Execute("DELETE FROM wired_item_configurations");
            var (original, _) = NativeMovementFresh(name);
            var initial = (original as IWiredConfiguredItem)?.Configuration;
            var store = new WiredConfigurationStore(db.Database);
            var handler = new Plus.Communication.Packets.Incoming.Rooms.Furni.Wired.SaveWiredEffectConfigEvent(new WiredConfigurationService(store, null!, TestLogging.For<WiredConfigurationService>()));
            db.Connection.Execute("CREATE TRIGGER reject_native_movement BEFORE INSERT ON wired_item_configurations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced native movement failure'");
            await Save();
            Assert.Equal(0, db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_item_configurations"));
            Assert.True(_room.GetWired().TryGet(601, out var current));
            Assert.Same(original, current);
            Assert.Same(initial, (current as IWiredConfiguredItem)?.Configuration);
            db.Connection.Execute("DROP TRIGGER reject_native_movement");
            await Save();
            Assert.True(_room.GetWired().TryGet(601, out current));
            var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(current);
            Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=601"));
            var durable = store.Load(601, configured.Descriptor)!;
            Assert.True(WiredNativeEditorProjection.IsBound(601, configured.Descriptor, durable));
            Assert.Equal(configured.Configuration.IntParams.ToArray(), durable.IntParams.ToArray());
            Assert.Equal(3, durable.Delay);
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
            Assert.True(_room.GetWired().TryRemove(601));
            var reloaded = Assert.IsAssignableFrom<IWiredConfiguredItem>(_room.GetWired().LoadWiredBox(original.Item));
            Assert.NotSame(configured, reloaded);
            Assert.Equal(durable.IntParams.ToArray(), reloaded.Configuration.IntParams.ToArray());
            reloaded.Item.Interactor.OnTrigger(_client, reloaded.Item, 0, true);
            Assert.Contains(_client.Packets, p => p.Header == ServerPacketHeader.WiredEffectConfigComposer);
            Task Save() => handler.Parse(_client, name == "wf_act_move_rotate"
                ? ClientPacket(601, 2, 5, 2, "", 0, 3, 1, 201, 0, 0, 0)
                : ClientPacket(601, 3, 2, 6, 1, "", 0, 3, 1, 201, 0, 0, 0));
        }
    }

    [WiredChestDatabaseFact]
    public async Task NativeMovementActualStoredV1NoopPreservesRawBytesThenChangedSaveKeepsInactiveData()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");

        foreach (var name in new[] { "wf_act_move_rotate", "wf_act_move_to_dir" }) {
            var runtime = new WiredConfiguration
            {
                IntParams = name == "wf_act_move_rotate" ? [2, 4, 201, 0] : [2, 6, 201, 1],
                Text = "inactive exact text",
                Delay = 3,
                FurniSources = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add("inactive", 200),
                UserSources = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add("unused", 201)
            };
            using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(runtime));
            var raw = "{\n" + string.Join(",\n ", document.RootElement.EnumerateObject().Reverse().Select(p => System.Text.Json.JsonSerializer.Serialize(p.Name) + ": " + p.Value.GetRawText())) + "\n}";
            db.Connection.Execute("DELETE FROM wired_item_configurations; INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES(601,@name,1,@raw)", new { name, raw });
            var (original, _) = NativeMovementFresh(name);
            var store = new WiredConfigurationStore(db.Database);
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
            Assert.True(_room.GetWired().TryRemove(601));
            var loaded = Assert.IsAssignableFrom<IWiredConfiguredItem>(_room.GetWired().LoadWiredBox(original.Item));
            var before = loaded.Configuration;
            var handler = new Plus.Communication.Packets.Incoming.Rooms.Furni.Wired.SaveWiredEffectConfigEvent(new WiredConfigurationService(store, null!, TestLogging.For<WiredConfigurationService>()));
            await handler.Parse(_client, name == "wf_act_move_rotate"
                ? ClientPacket(601, 2, 5, 2, "", 0, 3, 1, 201, 0, 0, 0)
                : ClientPacket(601, 3, 2, 6, 1, "", 0, 3, 1, 201, 0, 0, 0));
            Assert.Same(before, loaded.Configuration);
            Assert.Equal(raw, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601"));
            Assert.Contains(_client.Packets, p => p.Header == ServerPacketHeader.HideWiredConfigComposer);
            await handler.Parse(_client, name == "wf_act_move_rotate"
                ? ClientPacket(601, 2, 4, 1, "", 0, 3, 1, 200, 0, 0, 0)
                : ClientPacket(601, 3, 3, 5, 0, "", 0, 3, 1, 200, 0, 0, 0));
            var durable = store.Load(601, loaded.Descriptor)!;
            Assert.Equal(2, durable.Origin!.Native!.Version);
            Assert.Equal("inactive exact text", durable.Text);
            Assert.Equal(200, durable.Origin.Native.DormantLegacy!.FurniSources["inactive"]);
            Assert.Equal(201, durable.Origin.Native.DormantLegacy.UserSources["unused"]);
            Assert.Equal(200, durable.FurniSources["inactive"]);
            Assert.Equal(201, durable.UserSources["unused"]);
        }
    }

    [Fact]
    public void NativeMovementFreshRotateRightsMutationCannotSaveAReboundRequestedPick()
    {
        var (original, _) = NativeMovementFresh("wf_act_move_rotate");
        var pick = CanonicalPlace(602, 1, 1);
        var proof = _room.GetWired().CaptureLegacyRotate(original, () => true)!;
        Assert.NotNull(proof);
        var candidate = _room.GetWired().CreateConfiguredBox(original.Item, proof.Descriptor)!;
        var request = proof.Native with { OwnedIntParams = [5, 2], PrimaryItems = [new(602, false)] };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, proof.Descriptor, request, out var valid));
        var persisted = false;
        Assert.False(_room.GetWired().PublishLegacyRotate(proof, candidate, valid, () =>
        {
            pick.Definition.Type = ItemType.Wall;

            return true;
        }, () => persisted = true));
        Assert.False(persisted);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
    }

    [Theory]
    [InlineData("wf_act_move_rotate")]
    [InlineData("wf_act_move_to_dir")]
    public void NativeMovementStoredV1UnrepresentableFieldsRefuseNativeProjectionAndKeepRuntime(string name)
    {
        var (box, store) = CanonicalBox(name, new()
        {
            IntParams = name == "wf_act_move_rotate" ? [2, 4, 100, 0] : [2, 0, 100, 1],
            Text = "unchanged",
            SelectionCode = 1
        });
        var before = box.Configuration;
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        Assert.DoesNotContain(_client.Packets, p => p.Header == ServerPacketHeader.WiredEffectConfigComposer);
        Assert.False(WiredNativeEditorProjection.TryProject(box.Item, box.Descriptor, before, out _));
        Assert.True(WiredNativeEditorProjection.IsBound(box.Item.Id, box.Descriptor, before));
        Assert.Same(before, box.Configuration);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public void NativeMovementNonemptyConcreteRotateStillExecutesItsExistingTurn()
    {
        var (original, _) = NativeMovementFresh("wf_act_move_rotate");
        var floor = CanonicalPlace(602, 2, 2);
        original.SetItems[602] = floor;
        original.StringData = "0;1";
        Assert.True(original.Execute());
        Assert.Equal((2, 2, 2), (floor.GetX, floor.GetY, floor.Rotation));
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
    }

    [Fact]
    public void NativeMovementFreshDirectionValidProofPersistsBeforeInstallAndCannotBeReplayed()
    {
        var (original, _) = NativeMovementFresh("wf_act_move_to_dir");
        var box = (IWiredConfiguredItem)original;
        var initial = box.Configuration;
        var request = new WiredNativeEditorConfiguration
        { Category = WiredBoxCategory.Action, NativeCode = 13, OwnedIntParams = [0, 0, 1], FurniSourceTypes = [100], Delay = 0 };
        var proof = _room.GetWired().CaptureFreshDirection(original, request, () => true)!;
        Assert.NotNull(proof);
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, request, out var valid));
        var writes = 0;
        Assert.True(_room.GetWired().PublishFreshDirection(proof, valid, () => true, () =>
        {
            Assert.Same(initial, box.Configuration);
            writes++;
        }));
        Assert.Same(valid, box.Configuration);
        Assert.Throws<InvalidOperationException>(() => _room.GetWired().PublishFreshDirection(proof, valid, () => true, () => writes++));
        Assert.Equal(1, writes);
        Assert.Null(_room.GetWired().CaptureFreshDirection(original, request, () => true));
    }

    [Fact]
    public void NativeMovementFreshDirectionProofRejectsAnotherEqualRequestObject()
    {
        var (original, _) = NativeMovementFresh("wf_act_move_to_dir");
        var request = new WiredNativeEditorConfiguration
        { Category = WiredBoxCategory.Action, NativeCode = 13, OwnedIntParams = [0, 0, 1], FurniSourceTypes = [100], Delay = 0 };
        var proof = _room.GetWired().CaptureFreshDirection(original, request, () => true)!;
        Assert.NotNull(proof);
        Assert.True(WiredNativeEditorProjection.TryCompile(601, ((IWiredConfiguredItem)original).Descriptor, request with { }, out var valid));
        var writes = 0;
        Assert.Throws<InvalidOperationException>(() => _room.GetWired().PublishFreshDirection(proof, valid, () => true, () => writes++));
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeMovementRotateFinalRightsCannotResurrectRemovedOrReplacedRegistration(bool replace)
    {
        var (original, store) = NativeMovementFresh("wf_act_move_rotate");
        var wired = _room.GetWired();
        var proof = wired.CaptureLegacyRotate(original, () => true)!;
        Assert.NotNull(proof);
        var candidate = wired.CreateConfiguredBox(original.Item, proof.Descriptor)!;
        var request = proof.Native with { OwnedIntParams = [5, 2] };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, proof.Descriptor, request, out var valid));
        IWiredItem? replacement = null;
        var rightsCalls = 0;
        var result = WiredConfigurationSave.TrySave(candidate, valid, store, out _,
            publish: (_, validated, persist) => wired.PublishLegacyRotate(proof, candidate, validated, () =>
            {
                rightsCalls++;
                Assert.True(wired.TryRemove(original.Item.Id));

                if (replace) {
                    replacement = wired.LoadWiredBox(original.Item);
                    Assert.NotNull(replacement);
                    Assert.NotSame(original, replacement);
                }

                return true;
            }, persist));
        Assert.Empty(store.Saves);
        Assert.False(result);
        Assert.Equal(1, rightsCalls);
        Assert.Same(original.Item, _room.GetRoomItemHandler().GetItem(original.Item.Id));
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        Assert.Equal(replace, wired.TryGet(original.Item.Id, out var current));
        Assert.Same(replacement, current);
        Assert.NotSame(candidate, current);
    }

    private static WiredBoxDescriptor RotateDescriptor()
    {
        Assert.True(WiredBoxRegistry.TryGet("wf_act_move_rotate", out var descriptor));

        return descriptor;
    }

    private (IWiredItem Box, CanonicalStore Store) NativeMovementFresh(string name)
    {
        var store = new CanonicalStore(null);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
        var item = Furni(601, InteractionType.WiredEffect, name == "wf_act_move_rotate" ? WiredBoxType.EffectMoveAndRotate : WiredBoxType.None);
        item.RoomId = RoomId;
        item.Definition.InteractionName = name;
        item.Definition.ItemName = name;
        _room.GetRoomItemHandler().LoadFurniture([item]);
        Assert.True(_room.GetWired().TryGet(601, out var original));
        _client.Packets.Clear();

        return (original, store);
    }
}
