using System.Reflection;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Rooms;
using Dapper;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void NativeSaysFreshFactoryReopensWithoutInstallingAnAuthority()
    {
        var (original, store) = NativeSaysFresh();
        original.Item.Interactor.OnTrigger(_client, original.Item, 0, true);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredTriggeRconfigComposer);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.Empty(store.Saves);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0, 1)]
    [InlineData(0, 1, 1, 1, 1, 0)]
    public async Task NativeSaysFreshActualHandlerPersistsBeforePromotionAndReopens(int owner, int mode, int hide, int runtimeMode, int runtimeHide, int runtimeOwner)
    {
        var (original, store) = NativeSaysFresh();
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, owner, mode, hide, "pulse", 0, 0, 0, 0, 0));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(new[] { runtimeMode, runtimeHide, runtimeOwner }, saved.IntParams);
        Assert.Equal(0, saved.Delay);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.NotSame(original, current);
        var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(current);
        Assert.Equal(new[] { owner, mode, hide }, WiredEditorSnapshot.Capture(configured).Native!.OwnedIntParams);
        _client.Packets.Clear();
        configured.Item.Interactor.OnTrigger(_client, configured.Item, 0, true);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredTriggeRconfigComposer);
    }

    [Fact]
    public async Task NativeSaysRepresentableStoredV1NoopRetainsRuntimeIdentity()
    {
        var (box, store) = CanonicalBox("wf_trg_says_something", new() { IntParams = [1, 1, 0], Text = "pulse" });
        var before = box.Configuration;
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 1, 1, "pulse", 0, 0, 0, 0, 0));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        Assert.Empty(store.Saves);
        Assert.Same(before, box.Configuration);
    }

    [Theory]
    [InlineData(1, 0, 0, true, "near pulse", true, false)]
    [InlineData(1, 0, 0, false, "near pulse", false, false)]
    [InlineData(0, 1, 1, false, "pulse", true, true)]
    [InlineData(0, 1, 1, false, "near pulse", false, false)]
    [InlineData(0, 2, 1, false, "unrelated", true, true)]
    [InlineData(0, 2, 1, false, "", false, false)]
    public async Task NativeSaysActualOnChatUsesAsymmetricOwnerModeAndHide(int owner, int mode, int hide, bool isOwner, string message, bool matches, bool hides)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        NativeSaysFresh();
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, owner, mode, hide, "pulse", 0, 0, 0, 0, 0));
        AddSpeechBox(602, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "acted" }, 1, 0, 0);
        _room.OwnerId = isOwner ? 7 : 8;
        _client.Packets.Clear();
        actor.OnChat(0, message, false);
        Assert.Equal(matches, SpeechMessages(ServerPacketHeader.ChatComposer).Contains("acted"));
        Assert.Equal(hides ? new[] { message } : [], SpeechMessages(ServerPacketHeader.WhisperComposer));
    }

    [Theory]
    [InlineData("pulse")]
    [InlineData(" ")]
    public async Task NativeSaysNonemptyConcreteRemainsExecutableAndCannotBeEdited(string keyword)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (original, store) = NativeSaysFresh(keyword);
        original.Item.Interactor.OnTrigger(_client, original.Item, 0, true);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredTriggeRconfigComposer);
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 1, 1, "new", 0, 0, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.Equal(keyword, original.StringData);
        AddSpeechBox(602, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "legacy acted" }, 1, 0, 0);
        var concrete = Assert.IsType<Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserSaysBox>(original);
        Assert.Equal(keyword == "pulse", concrete.Execute(_client.GetHabbo(), "pulse"));
    }

    [Fact]
    public void NativeSaysCommandSubtypeNeverGetsAnEmptySaysDraft()
    {
        var (original, _) = NativeSaysFresh();
        var command = new Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserSaysCommandBox(_room, original.Item, TestWiredCommands.Unused);
        Assert.False(WiredNativeEditorProjection.TryCaptureLegacySays(command, out _));
        original.Item.Definition.WiredType = WiredBoxType.TriggerUserSaysCommand;
        Assert.False(WiredNativeEditorProjection.TryCaptureLegacySays(original, out _));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("bool")]
    [InlineData("items")]
    [InlineData("dictionary")]
    [InlineData("pick")]
    [InlineData("definition")]
    [InlineData("room")]
    [InlineData("rights")]
    [InlineData("detached")]
    public void NativeSaysStaleCapturedFactoryStateRefusesBeforePersistence(string change)
    {
        var (original, _) = NativeSaysFresh();
        var pick = CanonicalPlace(602, 1, 1);
        original.SetItems[602] = pick;
        var proof = _room.GetWired().CaptureLegacySays(original, () => true)!;
        Assert.NotNull(proof);
        var candidate = _room.GetWired().CreateConfiguredBox(original.Item, proof.Descriptor)!;
        Assert.True(WiredNativeEditorProjection.TryCompile(601, proof.Descriptor,
            proof.Native with { OwnedIntParams = [0, 1, 1], Text = "pulse" }, out var valid));

        switch (change) {
            case "text":
                original.StringData = " ";
                break;
            case "bool":
                original.BoolData = true;
                break;
            case "items":
                original.ItemsData = "changed";
                break;
            case "dictionary":
                original.SetItems = new(original.SetItems);
                break;
            case "pick":
                var replacement = Furni(602, InteractionType.None, WiredBoxType.None);
                Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, replacement, 1, 1, 0, true, false, false));
                original.SetItems[602] = replacement;
                break;
            case "definition":
                original.Item.Definition = new() { ItemName = "wf_trg_says_something" };
                break;
            case "room":
                original.Item.RoomId = 999;
                break;
            case "detached":
                _room.GetWired().TryRemove(original.Item.Id);
                break;
        }

        var persisted = false;
        Assert.Equal(WiredNativeSaveAdmission.Refused, _room.GetWired().AdmitLegacySays(proof, () => change != "rights"));
        Assert.False(_room.GetWired().PublishLegacySays(proof, candidate, valid, () => change != "rights", () => persisted = true));
        Assert.False(persisted);
    }

    [Theory]
    [InlineData("params")]
    [InlineData("stripped")]
    [InlineData("owner")]
    [InlineData("authority")]
    [InlineData("delay")]
    [InlineData("rebound")]
    public void NativeSaysAlteredAuthorityRefusesValidationApplySaveAndExecute(string change)
    {
        var (box, store) = CanonicalBox("wf_trg_says_something", new() { IntParams = [0, 0, 0], Text = "pulse" });
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, new()
        { Category = WiredBoxCategory.Trigger, OwnedIntParams = [1, 0, 0], Text = "pulse" }, out var valid));
        box.ApplyConfiguration(valid);
        var invalid = change switch
        {
            "params" => valid with { IntParams = [1, 0, 0] },
            "stripped" => new WiredConfiguration { IntParams = valid.IntParams, Text = valid.Text },
            "owner" => valid.Bind(valid.Origin! with { ItemId = 602 }),
            "authority" => valid.Bind(valid.Origin! with { Native = null }),
            "delay" => valid with { Delay = 1 },
            "rebound" => (valid with { IntParams = [1, 0, 0] }).Bind(valid.Origin! with { Derived = valid with { IntParams = [1, 0, 0] } }),
            _ => throw new InvalidOperationException()
        };
        Assert.False(box.TryValidateConfiguration(invalid, out _, out _));
        Assert.Throws<InvalidDataException>(() => box.ApplyConfiguration(invalid));
        Assert.Throws<ArgumentException>(() => new WiredConfigurationStore(_database).Save(601, box.Descriptor, invalid));
        Assert.False(WiredConfigurationSave.TrySave(box, invalid, store, out _));
        Assert.Empty(store.Saves);
        var captured = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Speech)
        { Actor = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused), Message = "pulse" }, captured.Targets, captured.Operations);
        ((Dictionary<uint, WiredConfiguration>)typeof(WiredRuntimeContext).GetField("_configurations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!)[601] = invalid;
        Assert.False(((WiredModernTrigger)box).Execute(context));
        Assert.Same(valid, box.Configuration);
    }

    [WiredChestDatabaseFact]
    public async Task NativeSaysActualDurableFirstSaveRollsBackThenReloadsItsCompleteAuthority()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");
        var (original, _) = NativeSaysFresh();
        original.BoolData = true;
        original.ItemsData = "retained raw inactive data";
        var pick = CanonicalPlace(602, 1, 1);
        original.SetItems[602] = pick;
        var store = new WiredConfigurationStore(db.Database);
        var handler = new SaveWiredTriggerConfigEvent(new WiredConfigurationService(store, null!, TestLogging.For<WiredConfigurationService>()));
        db.Connection.Execute("CREATE TRIGGER reject_native_says BEFORE INSERT ON wired_item_configurations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced native says failure'");
        await handler.Parse(_client, ClientPacket(601, 3, 1, 0, 0, "pulse", 1, 602, 0, 0, 0, 0));
        Assert.Equal(0, db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_item_configurations WHERE item_id=601"));
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.Equal("", original.StringData);
        db.Connection.Execute("DROP TRIGGER reject_native_says");
        await handler.Parse(_client, ClientPacket(601, 3, 1, 0, 0, "pulse", 1, 602, 0, 0, 0, 0));
        Assert.True(_room.GetWired().TryGet(601, out current));
        var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(current);
        Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=601"));
        var loaded = store.Load(601, configured.Descriptor)!;
        Assert.Equal(new[] { 0, 0, 1 }, loaded.IntParams);
        Assert.Equal(new uint[] { 602 }, loaded.SelectedItems);
        Assert.Equal("retained raw inactive data", loaded.Origin!.Native!.DormantLegacy!.LegacySaysItemsData);
        Assert.True(loaded.Origin.Native.DormantLegacy.LegacySaysBool);
        var json = db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601");
        Assert.True(configured.TryValidateConfiguration(loaded, out _, out _));
        var beforeNoop = configured.Configuration;
        await handler.Parse(_client, ClientPacket(601, 3, 1, 0, 0, "pulse", 1, 602, 0, 0, 0, 0));
        Assert.Same(beforeNoop, configured.Configuration);
        Assert.Equal(json, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601"));
    }

    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(0, 0, 2)]
    public async Task NativeSaysInvalidOwnedBitsRefuseActualFirstSave(int owner, int mode, int hide)
    {
        var (original, store) = NativeSaysFresh();
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, owner, mode, hide, "pulse", 0, 0, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredValidationErrorComposer);
    }

    [Fact]
    public async Task NativeSaysAllMatchStillEnforcesTextAndEmptyRoleVariableArrays()
    {
        var (original, store) = NativeSaysFresh();
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 2, 0, new string('x', 1001), 0, 0, 0, 0, 0));
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 2, 0, "", 0, 1, 100, 0, 0, 0));
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 2, 0, "", 0, 0, 0, 1, "custom:9", 0));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.Same(original, current);
    }

    [WiredChestDatabaseFact]
    public async Task NativeSaysStoredV1NoopKeepsDurableBytesPendingIdentityAndPublicationState()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (box, _) = CanonicalBox("wf_trg_says_something", new() { IntParams = [1, 1, 0], Text = "pulse" });
        var store = new WiredConfigurationStore(db.Database);
        store.Save(601, box.Descriptor, box.Configuration);
        var before = box.Configuration;
        var json = db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601");
        var item = CanonicalPlace(602, 0, 0);
        Assert.True(WiredBoxRegistry.TryGet("wf_act_control_clock", out var descriptor));
        var action = _room.GetWired().CreateConfiguredBox(item, descriptor)!;
        Assert.True(WiredNativeEditorProjection.TryCompile(602, action.Descriptor, new()
        { Category = WiredBoxCategory.Action, NativeCode = 28, OwnedIntParams = [0], FurniSourceTypes = [0], Delay = 5 }, out var delayed));
        action.ApplyConfiguration(delayed);
        Assert.True(_room.GetWired().AddBox(action));
        var engine = (WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetWired())!;
        Assert.True(engine.Enqueue(new(WiredEventKind.Speech) { Actor = actor, Message = "pulse" }));
        var pending = engine.ReadStats().Pending;
        Assert.True(pending > 0);
        var published = 0;
        engine.ConfigurationPublished = _ => published++;
        var handler = new SaveWiredTriggerConfigEvent(new WiredConfigurationService(store, null!, TestLogging.For<WiredConfigurationService>()));
        await handler.Parse(_client, ClientPacket(601, 3, 0, 1, 1, "pulse", 0, 0, 0, 0, 0));
        Assert.Same(before, box.Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        Assert.Equal(0, published);
        Assert.Equal(json, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601"));
        Assert.Equal(1, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=601"));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    [Fact]
    public void NativeSaysFinalRightsCallbackCannotUpgradeAnObsoleteConfigurationToNoop()
    {
        var (box, _) = CanonicalBox("wf_trg_says_something", new() { IntParams = [1, 1, 0], Text = "pulse" });
        var before = box.Configuration;
        var native = WiredEditorSnapshot.Capture(box).Native!;
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native with { OwnedIntParams = [1, 0, 0] }, out var after));
        var calls = 0;
        var result = _room.GetWired().TryAdmitUnchangedNativeSave(box, native, () =>
        {
            if (++calls == 2) {
                box.ApplyConfiguration(after);
            }

            return true;
        });
        Assert.Equal(WiredNativeSaveAdmission.Refused, result);
        Assert.NotSame(before, box.Configuration);
        Assert.Same(after, box.Configuration);
    }

    [Fact]
    public void NativeSaysFreshFinalRightsCallbackMustKeepItsEntireCaptureCurrent()
    {
        var (original, _) = NativeSaysFresh();
        var calls = 0;
        var captured = _room.GetWired().CaptureLegacySays(original, () =>
        {
            if (++calls == 2) {
                original.StringData = "pulse";
            }

            return true;
        });
        Assert.Null(captured);
    }

    [Fact]
    public async Task NativeSaysVisitorHasNoSaveRightsAndFirstDefaultSaveIsNotANoop()
    {
        var (original, store) = NativeSaysFresh();
        _room.OwnerId = 8;
        _room.OwnerName = "another-owner";
        _room.UsersWithRights = [];
        Assert.False(_room.GetWired().Settings.CanModify(_client));
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 0, 0, "", 0, 0, 0, 0, 0));
        Assert.Empty(store.Saves);
        _room.OwnerId = 7;
        _room.OwnerName = "owner";
        Assert.True(_room.GetWired().Settings.CanModify(_client));
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 0, 0, "", 0, 0, 0, 0, 0));
        Assert.Single(store.Saves);
        Assert.True(_room.GetWired().TryGet(601, out var current));
        Assert.NotSame(original, current);
    }

    [Fact]
    public async Task NativeSaysUnrepresentableStoredV1RetainsExecutionAndRefusesNativeEdit()
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (box, store) = CanonicalBox("wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "pulse", Delay = 1 });
        var before = box.Configuration;
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredTriggeRconfigComposer);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredValidationErrorComposer);
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, 0, 1, 0, "pulse", 0, 0, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.Same(before, box.Configuration);
        var captured = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Speech) { Actor = actor, Message = "pulse" }, captured.Targets, captured.Operations);
        Assert.True(((WiredModernTrigger)box).Execute(context));
    }

    [Fact]
    public void NativeSaysFreshReopenKeepsLegacyDraftSeparateFromBorrowedFooterDefaults()
    {
        var (original, _) = NativeSaysFresh();
        original.BoolData = true;
        original.Item.Interactor.OnTrigger(_client, original.Item, 0, true);
        var packet = Assert.Single(_client.Packets.Where(packet => packet.Header == ServerPacketHeader.WiredTriggeRconfigComposer));
        var (owned, defaults) = NativeSaysFooter(packet.Body);
        Assert.Equal(new[] { 1, 0, 0 }, owned);
        Assert.Equal(new[] { 0, 0, 1 }, defaults);
        Assert.True(original.BoolData);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    public async Task NativeSaysActualReopenFooterResetThenSaveUsesOwnerOffContainsHideOn(int mode, int hide, int owner)
    {
        var (box, store) = CanonicalBox("wf_trg_says_something", new() { IntParams = [mode, hide, owner], Text = "pulse" });
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        var packet = Assert.Single(_client.Packets.Where(packet => packet.Header == ServerPacketHeader.WiredTriggeRconfigComposer));
        var (owned, defaults) = NativeSaysFooter(packet.Body);
        Assert.Equal(new[] { owner, mode, hide }, owned);
        Assert.Equal(new[] { 0, 0, 1 }, defaults);
        await NativeSaysHandler().Parse(_client, ClientPacket(601, 3, defaults[0], defaults[1], defaults[2], "", 0, 0, 0, 0, 0));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(new[] { 0, 1, 0 }, saved.IntParams);
        Assert.Equal(new[] { 0, 0, 1 }, saved.Origin!.Native!.OwnedIntParams);
        Assert.Equal("", saved.Text);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    private static (int[] Owned, int[] Defaults) NativeSaysFooter(byte[] body)
    {
        var input = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = body };
        input.ReadInt(); // Furni limit.
        ReadInts(); // Dormant primary picks.
        ReadInts(); // Dormant secondary picks.
        input.ReadInt(); // Sprite.
        input.ReadUInt(); // Item routing identity.
        input.ReadString();
        var owned = ReadInts();
        Assert.Equal(0, input.ReadInt()); // Variable tokens.
        Assert.Empty(ReadInts()); // Furniture sources.
        Assert.Empty(ReadInts()); // User sources.
        Assert.Equal(0, input.ReadInt()); // Native Says code.
        Assert.True(input.ReadBool());
        Assert.Equal(0, input.ReadInt()); // Furniture role groups.
        Assert.Equal(0, input.ReadInt()); // User role groups.
        Assert.Empty(ReadInts()); // Default furniture sources.
        Assert.Empty(ReadInts()); // Default user sources.
        Assert.False(input.ReadBool()); // Wall picks unavailable.
        Assert.Equal(0, input.ReadInt()); // Contexts.
        var defaults = ReadInts();
        Assert.False(input.HasDataRemaining());

        return (owned, defaults);

        int[] ReadInts() => Enumerable.Range(0, input.ReadInt()).Select(_ => input.ReadInt()).ToArray();
    }

    private (IWiredItem Box, CanonicalStore Store) NativeSaysFresh(string text = "")
    {
        var store = new CanonicalStore(null);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_room.GetWired(), store);
        var item = Furni(601, InteractionType.WiredTrigger, WiredBoxType.TriggerUserSays);
        item.RoomId = RoomId;
        item.Definition.InteractionName = "wf_trg_says_something";
        item.Definition.ItemName = "wf_trg_says_something";
        _room.GetRoomItemHandler().LoadFurniture([item]);
        Assert.True(_room.GetWired().TryGet(601, out var original));
        Assert.IsType<Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserSaysBox>(original);
        original.StringData = text;
        _client.Packets.Clear();

        return (original, store);
    }

    private SaveWiredTriggerConfigEvent NativeSaysHandler() => new(CanonicalService((CanonicalStore)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
        .GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetWired())!));
}
