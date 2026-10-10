using System.Collections.Immutable;
using System.Reflection;
using Dapper;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using System.Text.Json;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    private const string RepeatCard = "wf_trg_periodically";
    private const string CountCard = "wf_cnd_user_count_in";
    private const string TeleportCard = "wf_act_teleport_to";

    [Theory]
    [InlineData(RepeatCard)]
    [InlineData(CountCard)]
    [InlineData(TeleportCard)]
    public void NativeRepeatCountTeleportActualPristineOpenIsPresentationOnly(string name)
    {
        var (box, store) = RctFresh(name);
        var engine = ThreeCardEngine();
        var pending = engine.ReadStats().Pending;
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        RctReply(Assert.Single(_client.Packets).Body, name, RctNative(name, defaults: true));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.Same(box, current);
        Assert.Equal(pending, engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(RepeatCard)]
    [InlineData(CountCard)]
    [InlineData(TeleportCard)]
    public async Task NativeRepeatCountTeleportActualHandlerSaveReopen(string name)
    {
        var (original, store) = RctFresh(name);
        var native = RctNative(name);
        await RctSave(store, name, ThreeCardPacket(native));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(name == RepeatCard ? new[] { 4 } : name == CountCard ? new[] { 2, 7, 0 } : new[] { 1, 100, 201 }, saved.IntParams);
        Assert.Equal(name == TeleportCard ? 4 : 0, saved.Delay);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.NotSame(original, current);
        Assert.Contains(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        _client.Packets.Clear();
        current.Item.Interactor.OnTrigger(_client, current.Item, 0, true);
        RctReply(Assert.Single(_client.Packets).Body, name, native);
    }

    [Theory]
    [InlineData(RepeatCard)]
    [InlineData(CountCard)]
    [InlineData(TeleportCard)]
    public void NativeRepeatCountTeleportActualFactoryFreshOpenDoesNotMintAuthority(string name)
    {
        var (concrete, store) = RctFresh(name);
        var wired = _room.GetWired();
        Assert.True(wired.TryRemove(701));
        var factory = wired.CreateConfiguredBox(concrete.Item)!;
        var initial = factory.Configuration;
        wired.AddBox(factory);
        factory.Item.Interactor.OnTrigger(_client, factory.Item, 0, true);
        RctReply(Assert.Single(_client.Packets).Body, name, RctNative(name, defaults: true));
        Assert.Same(initial, factory.Configuration);
        Assert.Null(initial.Origin);
        Assert.Empty(store.Saves);
    }

    [Theory]
    [InlineData(RepeatCard, false)]
    [InlineData(CountCard, false)]
    [InlineData(TeleportCard, false)]
    [InlineData(RepeatCard, true)]
    [InlineData(CountCard, true)]
    [InlineData(TeleportCard, true)]
    public async Task NativeRepeatCountTeleportFirstDefaultChangedThenOnlySecondSaveNoop(string name, bool factory)
    {
        var (original, store) = RctFresh(name);

        if (factory) {
            original = RctFactory(original);
        }

        var initial = (original as IWiredConfiguredItem)?.Configuration;
        var timing = (original as WiredModernTimedTrigger)?.InitialCardTiming;
        var native = RctNative(name, defaults: true);
        store.BeforeSave = () =>
        {
            Assert.True(_room.GetWired().TryGet(701, out var registered));
            Assert.Same(original, registered);

            if (initial != null) {
                Assert.Same(initial, ((IWiredConfiguredItem)original).Configuration);
            }

            Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        };
        await RctSave(store, name, ThreeCardPacket(native));
        Assert.Single(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var installed));
        var config = ((IWiredConfiguredItem)installed).Configuration;
        var raw = store.Json;
        var engine = ThreeCardEngine();
        Assert.True(engine.Enqueue(new(WiredEventKind.Speech) { Message = "pending" }));
        var pending = engine.ReadStats().Pending;

        if (factory) {
            Assert.Null(_room.GetWired().CaptureFreshCard(original, native, () => true));
            Assert.Throws<InvalidDataException>(() => ((WiredModernBox)original).ApplyConfiguration(initial!));
            Assert.Same(config, ((IWiredConfiguredItem)installed).Configuration);
        }

        store.BeforeSave = () => throw new InvalidOperationException("No-op write");
        var repeatTiming = (installed as WiredModernTimedTrigger)?.InitialCardTiming;
        await RctSave(store, name, ThreeCardPacket(native));
        Assert.Single(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(config, ((IWiredConfiguredItem)installed).Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        Assert.Equal(repeatTiming, (installed as WiredModernTimedTrigger)?.InitialCardTiming);
    }

    public static IEnumerable<object[]> RctProofCases() =>
        from name in new[] { RepeatCard, CountCard, TeleportCard }
        from factory in new[] { false, true }
        from change in new[] { "valid", "rights", "remove", "replace", "dictionary", "raw", "bool", "definition", "kind", "box-x", "box-z-bits", "pick-y", "pick-z", "pick-definition", "pick-temporary" }
        select new object[] { name, factory, change };

    [Theory]
    [MemberData(nameof(RctProofCases))]
    public void NativeRepeatCountTeleportFinalRightsMustRetainExactOriginalAndEveryPick(string name, bool factory, string change)
    {
        var (box, store) = RctFresh(name);

        if (factory) {
            box = RctFactory(box);
        }

        var wired = _room.GetWired();
        var request = RctNative(name) with { PrimaryItems = [new(702, false)], SecondaryItems = [new(703, false)] };
        var pristine = factory ? null : wired.CapturePristineCard(box, request, () => true);
        var fresh = factory ? wired.CaptureFreshCard(box, request, () => true) : null;
        Assert.True(pristine != null || fresh != null);
        var descriptor = pristine?.Descriptor ?? fresh!.Descriptor;
        var candidate = factory ? (IWiredConfiguredItem)box : wired.CreateConfiguredBox(box.Item, descriptor)!;
        Assert.True(WiredNativeEditorProjection.TryCompile(701, descriptor, request, out var runtime));
        var picked = _room.GetRoomItemHandler().GetItem(703)!;
        IWiredItem? replacement = null;
        var calls = 0;
        bool Rights()
        {
            calls++;

            switch (change) {
                case "remove":
                case "replace":
                    Assert.True(wired.TryRemove(701));

                    if (change == "replace") {
                        replacement = wired.CreateConfiguredBox(box.Item)!;
                        wired.AddBox(replacement);
                    }

                    break;
                case "dictionary":
                    box.SetItems = new();
                    break;
                case "raw":
                    box.StringData = " ";
                    break;
                case "bool":
                    box.BoolData = true;
                    break;
                case "definition":
                    box.Item.Definition.Id++;
                    break;
                case "kind":
                    box.Item.Definition.WiredType = WiredBoxType.None;
                    break;
                case "box-x":
                    box.Item.GetX++;
                    break;
                case "box-z-bits":
                    box.Item.GetZ = BitConverter.Int64BitsToDouble(long.MinValue);
                    break;
                case "pick-y":
                    picked.GetY++;
                    break;
                case "pick-z":
                    picked.GetZ++;
                    break;
                case "pick-definition":
                    picked.Definition.Id++;
                    break;
                case "pick-temporary":
                    typeof(Item).GetProperty(nameof(Item.IsTemporary))!.SetValue(picked, true);
                    break;
            }

            return change != "rights";
        }
        var saved = RctPublisherRefused(() => WiredConfigurationSave.TrySave(candidate, runtime, store, out _, publish: (_, validated, persist) => factory
            ? wired.PublishFreshCard(fresh!, validated, Rights, persist)
            : wired.PublishPristineCard(pristine!, candidate, validated, Rights, persist)));
        Assert.Equal(change == "valid", saved);
        Assert.Equal(1, calls);

        if (change == "valid") {
            Assert.Single(store.Saves);
        }
        else {
            Assert.Empty(store.Saves);
        }

        if (change is "remove" or "replace") {
            Assert.Equal(change == "replace", wired.TryGet(701, out var registered));
            Assert.Same(replacement, registered);
        }
        else if (change != "valid") {
            Assert.True(wired.TryGet(701, out var registered));
            Assert.Same(box, registered);
        }

        if (change == "box-x") {
            Assert.Equal(1, box.Item.GetX);
        }

        if (change == "box-z-bits") {
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(box.Item.GetZ));
        }

        if (change == "pick-y") {
            Assert.Equal(1, picked.GetY);
        }

        if (change == "pick-z") {
            Assert.Equal(1d, picked.GetZ);
        }

        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRepeatCountTeleportModernRepeatTimingProofDoesNotAssumeZeroAndRejectsReset(bool fullReset)
    {
        var (box, store) = RctFresh(RepeatCard);
        var timer = (WiredModernTimedTrigger)RctFactory(box);
        var initial = timer.Configuration;
        var initialTiming = timer.InitialCardTiming;
        Assert.True(initialTiming.Epoch > 0);
        Assert.Null(timer.Poll(long.MaxValue));
        Assert.Equal(initialTiming, timer.InitialCardTiming);
        var request = RctNative(RepeatCard);
        var proof = _room.GetWired().CaptureFreshCard(timer, request, () => true)!;
        Assert.NotNull(proof);
        Assert.True(WiredNativeEditorProjection.TryCompile(701, timer.Descriptor, request, out var runtime));
        var saved = RctPublisherRefused(() => WiredConfigurationSave.TrySave(timer, runtime, store, out _, publish: (_, validated, persist) =>
            _room.GetWired().PublishFreshCard(proof, validated, () =>
            {
                if (fullReset) {
                    timer.Reset(123);
                }
                else {
                    timer.ResetElapsed(123);
                }

                return true;
            }, persist)));
        Assert.False(saved);
        Assert.Empty(store.Saves);
        Assert.Same(initial, timer.Configuration);
        Assert.Equal((123L, initialTiming.Epoch + 1), timer.InitialCardTiming);
    }

    [Theory]
    [InlineData(RepeatCard, "tick")]
    [InlineData(RepeatCard, "delay")]
    [InlineData(TeleportCard, "tick")]
    [InlineData(TeleportCard, "delay")]
    public void NativeRepeatCountTeleportConcreteCycleProofRequiresActualZeroDelayAndTick(string name, string mutation)
    {
        var (box, store) = RctFresh(name);
        var proof = _room.GetWired().CapturePristineCard(box, RctNative(name), () => true)!;
        Assert.NotNull(proof);

        if (mutation == "tick") {
            ((IWiredCycle)box).TickCount = 1;
        }
        else if (box is Plus.HabboHotel.Items.Wired.Boxes.Effects.TeleportUserBox teleport) {
            teleport.Delay = 0;
        }
        else {
            ((Plus.HabboHotel.Items.Wired.Boxes.Triggers.RepeaterBox)box).Delay = 1;
        }

        Assert.Null(_room.GetWired().CapturePristineCard(box, null, () => true));
        Assert.False(proof.Matches());
        Assert.Empty(store.Saves);
    }

    [Theory]
    [InlineData(RepeatCard)]
    [InlineData(CountCard)]
    [InlineData(TeleportCard)]
    public async Task NativeRepeatCountTeleportRealStoredV1NoopPreservesRawDormantAndPending(string name)
    {
        var prior = new WiredConfiguration
        {
            IntParams = name == RepeatCard ? [4] : name == CountCard ? [2, 7, 0] : [1, 100, 201],
            Text = "  dormant raw\r\n  ",
            Delay = name == TeleportCard ? 4 : 0,
            SelectedItems = [702, 703],
            SecondarySelectedItems = [703],
            FurniSources = name == TeleportCard ? ImmutableDictionary<string, int>.Empty.Add("targets", 100).Add("dormant", 900) : ImmutableDictionary<string, int>.Empty.Add("dormant", 900),
            UserSources = name == TeleportCard ? ImmutableDictionary<string, int>.Empty.Add("users", 201).Add("dormant", 900) : ImmutableDictionary<string, int>.Empty.Add("dormant", 900),
            Snapshots = [new(702, 0, 1, 1, 0, 0, "raw")]
        };
        var raw = JsonSerializer.Serialize(prior, new JsonSerializerOptions { WriteIndented = true });
        var (loaded, store) = RctFresh(name, raw);
        var configured = (IWiredConfiguredItem)loaded;
        var original = configured.Configuration;
        var timing = (loaded as WiredModernTimedTrigger)?.InitialCardTiming;
        Assert.Equal(WiredConfigurationOriginKind.StoredLegacy, original.Origin!.Kind);
        Assert.Null(original.Origin.Native);
        Assert.True(WiredNativeEditorProjection.TryProject(loaded.Item, configured.Descriptor, original, out var native));
        Assert.Equal("", native.Text);
        var engine = ThreeCardEngine();
        engine.Enqueue(new(WiredEventKind.Speech) { Message = "pending" });
        var pending = engine.ReadStats().Pending;
        await RctSave(store, name, ThreeCardPacket(native));
        Assert.Empty(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(original, configured.Configuration);
        Assert.Equal(timing, (loaded as WiredModernTimedTrigger)?.InitialCardTiming);
        Assert.Equal(pending, engine.ReadStats().Pending);
        var changed = native with { OwnedIntParams = name == RepeatCard ? [5] : name == CountCard ? [1, 7] : [0] };
        await RctSave(store, name, ThreeCardPacket(changed));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(prior.Text, saved.Text);
        Assert.Equal(prior.Snapshots.ToArray(), saved.Snapshots.ToArray());
        Assert.Equal(900, saved.FurniSources["dormant"]);
        Assert.Equal(900, saved.UserSources["dormant"]);
    }

    [Theory]
    [InlineData(RepeatCard, 0)]
    [InlineData(RepeatCard, 121)]
    [InlineData(CountCard, 126)]
    [InlineData(TeleportCard, 0)]
    [InlineData(TeleportCard, 200)]
    [InlineData(TeleportCard, 201)]
    [InlineData(TeleportCard, 11)]
    public async Task NativeRepeatCountTeleportUnrepresentableStoredV1PreservesInstalledRow(string name, int value)
    {
        var prior = new WiredConfiguration
        {
            IntParams = name == RepeatCard ? [value] : name == CountCard ? [0, value, 0]
            : value == 11 ? [0, 100, 11] : [0, value, 0],
            SelectedItems = [702]
        };
        var raw = JsonSerializer.Serialize(prior);

        if (name == RepeatCard) {
            var (concrete, refusedStore) = RctFresh(name);
            var descriptor = _room.GetWired().CreateConfiguredBox(concrete.Item)!.Descriptor;
            var rows = new ModernWiredRuntimeTests.StoredRuntimeRowsDatabase([new(701, name, 1, raw)]);
            var loaded = new WiredConfigurationStore(rows).Load(701, descriptor)!;
            Assert.Equal(prior.IntParams.ToArray(), loaded.IntParams.ToArray());
            Assert.False(((IWiredConfiguredItem)_room.GetWired().CreateConfiguredBox(concrete.Item)!).TryValidateConfiguration(loaded, out _, out _));
            Assert.Throws<InvalidDataException>(() => WiredBoxLoading.Select(concrete, _room.GetWired().CreateConfiguredBox(concrete.Item), loaded));
            Assert.Empty(refusedStore.Saves);

            return;
        }

        var (box, store) = RctFresh(name, raw);
        var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(box);
        var original = configured.Configuration;
        Assert.False(WiredNativeEditorProjection.TryProject(box.Item, configured.Descriptor, original, out _));
        await RctSave(store, name, ThreeCardPacket(RctNative(name)));
        Assert.Empty(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(original, configured.Configuration);
        Assert.Contains(ServerPacketHeader.WiredValidationErrorComposer, _client.Sent);
    }

    [WiredChestDatabaseFact]
    public async Task NativeRepeatCountTeleportActualSqlRefusalThenDurableReloadAllCategories()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");

        foreach (var name in new[] { RepeatCard, CountCard, TeleportCard }) {
            db.Connection.Execute("DELETE FROM wired_item_configurations");
            var (box, _) = RctFresh(name);
            var writes = 0;
            var store = new RctDurableStore(new WiredConfigurationStore(db.Database), () =>
            {
                Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
                Assert.True(_room.GetWired().TryGet(701, out var beforeInstall));
                Assert.Same(box, beforeInstall);
                Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
                writes++;
            });
            db.Connection.Execute("CREATE TRIGGER reject_rct BEFORE INSERT ON wired_item_configurations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced rct'");
            await RctSave(store, name, ThreeCardPacket(RctNative(name, defaults: true)));
            Assert.Equal(0, db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_item_configurations"));
            Assert.True(_room.GetWired().TryGet(701, out var original));
            Assert.Same(box, original);
            Assert.Equal(0, writes);
            Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
            db.Connection.Execute("DROP TRIGGER reject_rct");
            await RctSave(store, name, ThreeCardPacket(RctNative(name, defaults: true)));
            Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
            Assert.True(_room.GetWired().TryGet(701, out var installed));
            var current = (IWiredConfiguredItem)installed;
            var reloaded = store.Load(701, current.Descriptor)!;
            Assert.True(WiredNativeEditorProjection.Matches(current.Configuration, reloaded));
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
            Assert.True(_room.GetWired().TryRemove(701));
            var loaded = _room.GetWired().LoadWiredBox(box.Item);
            _client.Packets.Clear();
            loaded.Item.Interactor.OnTrigger(_client, loaded.Item, 0, true);
            RctReply(Assert.Single(_client.Packets).Body, name, RctNative(name, defaults: true));
            var raw = db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701");
            Console.WriteLine("RCT_SQL " + JsonSerializer.Serialize(new { name, stage = "default-schema2", raw }));
            await RctSave(store, name, ThreeCardPacket(RctNative(name, defaults: true)));
            Assert.Equal(raw, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701"));
            Assert.Equal(1, writes);
        }
    }

    public static IEnumerable<object[]> RctMalformedCases() =>
        from name in new[] { RepeatCard, CountCard, TeleportCard }
        from change in new[] { "owned-missing", "owned-extra", "range", "text", "variables", "furni-extra", "users-extra", "secondary-wall", "trailing", "truncated" }
        select new object[] { name, change };

    [Theory]
    [MemberData(nameof(RctMalformedCases))]
    public async Task NativeRepeatCountTeleportActualHandlerRejectsMalformedWithoutChangingInstalledState(string name, string change)
    {
        var (_, store) = RctFresh(name);
        var valid = RctNative(name);
        await RctSave(store, name, ThreeCardPacket(valid));
        Assert.Single(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var original = ((IWiredConfiguredItem)box).Configuration;
        var invalid = change switch
        {
            "owned-missing" => valid with { OwnedIntParams = [] },
            "owned-extra" => valid with { OwnedIntParams = valid.OwnedIntParams.Add(0) },
            "range" => valid with { OwnedIntParams = valid.OwnedIntParams.SetItem(0, 126) },
            "text" => valid with { Text = " " },
            "variables" => valid with { VariableIds = ["variable"] },
            "furni-extra" => valid with { FurniSourceTypes = valid.FurniSourceTypes.Add(100) },
            "users-extra" => valid with { UserSourceTypes = valid.UserSourceTypes.Add(0) },
            "secondary-wall" => valid with { SecondaryItems = [new(703, true)] },
            _ => valid
        };
        var bytes = ThreeCardPacket(invalid).Buffer.ToArray();

        if (change == "trailing") {
            bytes = bytes.Concat(new byte[] { 0 }).ToArray();
        }

        if (change == "truncated") {
            bytes = bytes[..^1];
        }

        _client.Sent.Clear();
        await RctSave(store, name, new() { Buffer = bytes });
        Assert.Single(store.Saves);
        Assert.Same(original, ((IWiredConfiguredItem)box).Configuration);
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
    }

    [Theory]
    [InlineData(RepeatCard, 1)]
    [InlineData(RepeatCard, 120)]
    [InlineData(CountCard, 0)]
    [InlineData(CountCard, 125)]
    [InlineData(TeleportCard, 0)]
    [InlineData(TeleportCard, 1)]
    public async Task NativeRepeatCountTeleportActualHandlerAcceptsExactBoundaryFields(string name, int value)
    {
        var (_, store) = RctFresh(name);
        var native = RctNative(name) with { OwnedIntParams = name == CountCard ? [value, value] : [value] };
        await RctSave(store, name, ThreeCardPacket(native));
        Assert.Single(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var box));
        _client.Packets.Clear();
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        RctReply(Assert.Single(_client.Packets).Body, name, native);
        Console.WriteLine("RCT_BODY " + name + " " + Convert.ToBase64String(_client.Packets[0].Body));
    }

    [Theory]
    [InlineData(0, 11)]
    [InlineData(900, 0)]
    [InlineData(101, 0)]
    public async Task NativeRepeatCountTeleportHandlerRefusesUnadvertisedTeleportSources(int furni, int users)
    {
        var (original, store) = RctFresh(TeleportCard);
        await RctSave(store, TeleportCard, ThreeCardPacket(RctNative(TeleportCard) with { FurniSourceTypes = [furni], UserSourceTypes = [users] }));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.Same(original, current);
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(2, 1, 0)]
    public async Task NativeRepeatCountTeleportCountQuantifierIsSeparateAndInvertedRangeRefuses(int min, int max, int quantifier)
    {
        var (original, store) = RctFresh(CountCard);
        await RctSave(store, CountCard, ThreeCardPacket(RctNative(CountCard) with { OwnedIntParams = [min, max], Quantifier = quantifier }));
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.Same(original, current);
        Assert.Contains(ServerPacketHeader.WiredValidationErrorComposer, _client.Sent);
    }

    [Fact]
    public async Task NativeRepeatCountTeleportRepeatRetainsPrimingLatePollAndResetTiming()
    {
        var (_, store) = RctFresh(RepeatCard);
        await RctSave(store, RepeatCard, ThreeCardPacket(RctNative(RepeatCard)));
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var timer = Assert.IsType<WiredModernTimedTrigger>(box);
        Assert.Null(timer.Poll(0));
        Assert.Null(timer.Poll(1999));
        Assert.NotNull(timer.Poll(2000));
        Assert.NotNull(timer.Poll(9000));
        Assert.Null(timer.Poll(9001));
        timer.ResetElapsed(9500);
        Assert.NotNull(timer.Poll(11000));
        timer.Reset(11000);
        Assert.Null(timer.Poll(11000));
        Assert.Null(timer.Poll(12999));
        Assert.NotNull(timer.Poll(13000));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 2, false)]
    public async Task NativeRepeatCountTeleportCountRetainsInclusiveRoomNonbotPool(int min, int max, bool expected)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (_, store) = RctFresh(CountCard);
        await RctSave(store, CountCard, ThreeCardPacket(RctNative(CountCard) with { OwnedIntParams = [min, max] }));
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var frame = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Use) { Actor = actor }, frame.Targets, frame.Operations);
        Assert.Equal(expected, ((WiredModernBox)box).Execute(context));
        actor.BotData = (Plus.HabboHotel.Rooms.AI.RoomBot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plus.HabboHotel.Rooms.AI.RoomBot));
        Assert.Equal(min == 0 && max == 0, ((WiredModernBox)box).Execute(context));
        actor.BotData = null;
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(200, false, false, false)]
    [InlineData(201, false, false, false)]
    [InlineData(0, true, false, false)]
    [InlineData(200, true, false, false)]
    [InlineData(201, true, false, false)]
    [InlineData(0, false, true, false)]
    [InlineData(0, false, false, true)]
    [InlineData(0, false, true, true)]
    public async Task NativeRepeatCountTeleportNativeWallTargetsCannotChooseOrThawWhileStoredTriggerKeepsRuntime(int source, bool floorPresent, bool legacy, bool called)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var raw = legacy ? JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, source, 0], FurniSources = ImmutableDictionary<string, int>.Empty.Add("targets", source), UserSources = ImmutableDictionary<string, int>.Empty.Add("users", 0) }) : null;
        var (original, store) = RctFresh(TeleportCard, raw);

        if (!legacy) {
            await RctSave(store, TeleportCard, ThreeCardPacket(RctNative(TeleportCard) with { OwnedIntParams = [1], FurniSourceTypes = [source], UserSourceTypes = [0], Delay = 0 }));
        }

        Assert.True(_room.GetWired().TryGet(701, out var box));
        var wall = Furni(704, InteractionType.None, WiredBoxType.None, Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Wall);
        wall.RoomId = RoomId;
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, wall));
        wall.GetX = actor.X;
        wall.GetY = actor.Y;
        var floor = _room.GetRoomItemHandler().GetItem(702)!;
        floor.GetX = actor.X;
        floor.GetY = actor.Y;
        var frame = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Use) { Actor = actor, EventItem = wall }, frame.Targets, frame.Operations);
        context.Triggering.UserIds.Add(actor.VirtualId);
        context.Triggering.FurniIds.Add(wall.Id);
        context.SelectorPool.FurniIds.Add(wall.Id);
        var selection = new WiredSelection();
        selection.FurniIds.Add(wall.Id);

        if (floorPresent) {
            context.Triggering.FurniIds.Add(floor.Id);
            context.SelectorPool.FurniIds.Add(floor.Id);
            selection.FurniIds.Add(floor.Id);
        }

        context.Signal = new(selection, new Dictionary<string, long>());
        var state = WiredAvatarState.For(_room);
        state.FreezeUser(actor, 0, cancelOnTeleport: true);
        _client.Packets.Clear();

        if (called) {
            selection.UserIds.Add(actor.VirtualId);
            context.Selected = selection;
            Assert.True(ThreeCardEngine().CallStacks(context, [box.Item]));
            ThreeCardEngine().OnCycle();
        }
        else {
            Assert.Equal(legacy || floorPresent, ((WiredModernBox)box).Execute(context));
        }

        Assert.Equal(!legacy && !floorPresent, actor.Frozen);
        Assert.Equal(!legacy && !floorPresent, !actor.CanWalk);

        if (called) {
            // The normal engine cycle also flushes prepared clock items; none is movement/FX.
            Assert.NotEmpty(_client.Packets);
            Assert.All(_client.Packets, packet => Assert.Equal(ServerPacketHeader.ObjectUpdateComposer, packet.Item1));
        }
        else {
            Assert.Empty(_client.Packets);
        }

        if (legacy) {
            var config = ((IWiredConfiguredItem)box).Configuration;
            Assert.False(WiredNativeEditorProjection.TryProject(box.Item, ((IWiredConfiguredItem)box).Descriptor, config, out _));
            await RctSave(store, TeleportCard, ThreeCardPacket(RctNative(TeleportCard)));
            Assert.Equal(raw, store.Json);
            Assert.Empty(store.Saves);
            Assert.Same(config, ((IWiredConfiguredItem)box).Configuration);
        }
    }

    [Theory]
    [InlineData(0, "normal")]
    [InlineData(1, "normal")]
    [InlineData(0, "blocked")]
    [InlineData(0, "solid")]
    [InlineData(0, "freeze-cancel")]
    [InlineData(0, "freeze-keep")]
    public async Task NativeRepeatCountTeleportActualMapOwnerRetainsRelocationDurationTerrainAndFreeze(int fast, string scenario)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (_, store) = RctFresh(TeleportCard);
        var target = _room.GetRoomItemHandler().GetItem(702)!;
        target.Definition.Walkable = scenario != "solid";
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, target, 1, 1, 0, false, false, false));
        Assert.Equal((1, 1), (target.GetX, target.GetY));

        if (scenario == "blocked") {
            _room.GetGameMap().Model.SqState[1, 1] = Plus.HabboHotel.Rooms.SquareState.Blocked;
        }

        if (scenario.StartsWith("freeze")) {
            WiredAvatarState.For(_room).FreezeUser(actor, 0, cancelOnTeleport: scenario == "freeze-cancel");
        }

        var request = RctNative(TeleportCard) with { OwnedIntParams = [fast], PrimaryItems = [new(702, false)], SecondaryItems = [], UserSourceTypes = [0], Delay = 0 };
        await RctSave(store, TeleportCard, ThreeCardPacket(request));
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var frame = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Use) { Actor = actor }, frame.Targets, frame.Operations);
        context.Triggering.UserIds.Add(actor.VirtualId);
        _client.Packets.Clear();
        var moved = false;

        if (_room.UsesV2Movement) {
            _room.RunFastPass(() => moved = ((WiredModernBox)box).Execute(context));
        }
        else {
            moved = ((WiredModernBox)box).Execute(context);
        }

        Console.WriteLine("RCT_MOTION usesV2=" + _room.UsesV2Movement);
        var expected = scenario is not ("blocked" or "solid");
        Assert.Equal(expected, moved);
        Assert.Equal(expected ? (1, 1) : (0, 0), (actor.X, actor.Y));
        Assert.Equal(scenario == "freeze-keep", actor.Frozen);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.AvatarEffectComposer);

        if (!expected) {
            Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredMovementsComposer);

            return;
        }

        var movement = Assert.Single(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredMovementsComposer);
        var wire = new FlashIncomingPacket { Buffer = movement.Body };
        Assert.Equal(1, wire.ReadInt());
        Assert.Equal(0, wire.ReadInt());
        Assert.Equal(new[] { 0, 0, 1, 1 }, Enumerable.Range(0, 4).Select(_ => wire.ReadInt()).ToArray());
        wire.ReadString();
        wire.ReadString();
        Assert.Equal(actor.VirtualId, wire.ReadInt());
        Assert.Equal(1, wire.ReadInt());
        Assert.Equal(fast == 1 ? 0 : 500, wire.ReadInt());
        Assert.Equal(actor.RotBody, wire.ReadInt());
        Assert.Equal(actor.RotHead, wire.ReadInt());
        Assert.Equal(0, wire.ReadByte());
        Assert.False(wire.HasDataRemaining());
    }

    [Theory]
    [InlineData(RepeatCard)]
    [InlineData(CountCard)]
    [InlineData(TeleportCard)]
    public async Task NativeRepeatCountTeleportBoundOriginCannotBeStrippedAlteredOrRebound(string name)
    {
        var (_, store) = RctFresh(name);
        await RctSave(store, name, ThreeCardPacket(RctNative(name)));
        Assert.True(_room.GetWired().TryGet(701, out var registered));
        var box = Assert.IsAssignableFrom<WiredModernBox>(registered);
        var current = box.Configuration;

        foreach (var invalid in new[] {
            JsonSerializer.Deserialize<WiredConfiguration>(JsonSerializer.Serialize(current))!,
            current with { IntParams = current.IntParams.SetItem(0, 0) },
            current with { Text = "altered" }
        }) {
            Assert.Throws<InvalidDataException>(() => box.ApplyConfiguration(invalid));
            Assert.Same(current, box.Configuration);
        }

        var other = _room.GetWired().CreateConfiguredBox(_room.GetRoomItemHandler().GetItem(702)!, box.Descriptor)!;
        Assert.Throws<InvalidDataException>(() => other.ApplyConfiguration(current));
        Assert.True(_room.GetWired().TryGet(701, out var retained));
        Assert.Same(box, retained);
        Assert.Single(store.Saves);
    }

    [Theory]
    [InlineData(RepeatCard)]
    [InlineData(CountCard)]
    [InlineData(TeleportCard)]
    public async Task NativeRepeatCountTeleportNonpristineConcreteRefusesEditorWithoutMutation(string name)
    {
        var (box, store) = RctFresh(name);
        box.StringData = name == CountCard ? "0;125" : "dormant";
        var dictionary = box.SetItems;
        var result = box.Execute(new object());
        Assert.Equal(name != TeleportCard, result);
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        Assert.Contains(ServerPacketHeader.WiredValidationErrorComposer, _client.Sent);
        await RctSave(store, name, ThreeCardPacket(RctNative(name)));
        Assert.Empty(store.Saves);
        Assert.Same(dictionary, box.SetItems);
        Assert.Equal(name == CountCard ? "0;125" : "dormant", box.StringData);
        Assert.True(_room.GetWired().TryGet(701, out var retained));
        Assert.Same(box, retained);
        Assert.Equal(result, box.Execute(new object()));
    }

    [WiredChestDatabaseFact]
    public async Task NativeRepeatCountTeleportActualSqlV1RawNoopThenChangedDormantSave()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");

        foreach (var name in new[] { RepeatCard, CountCard, TeleportCard }) {
            db.Connection.Execute("DELETE FROM wired_item_configurations");
            var prior = new WiredConfiguration
            {
                IntParams = name == RepeatCard ? [4] : name == CountCard ? [2, 7, 0] : [1, 100, 0],
                Text = "  dormant\r\n  ",
                SelectedItems = [702],
                SecondarySelectedItems = [703],
                FurniSources = ImmutableDictionary<string, int>.Empty.Add("dormant", 900),
                UserSources = ImmutableDictionary<string, int>.Empty.Add("dormant", 900)
            };
            var raw = JsonSerializer.Serialize(prior, new JsonSerializerOptions { WriteIndented = true });
            db.Connection.Execute("INSERT INTO wired_item_configurations (item_id,box_name,schema_version,configuration) VALUES (701,@name,1,@raw)", new { name, raw });
            var (concrete, _) = RctFresh(name);
            var store = new WiredConfigurationStore(db.Database);
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
            Assert.True(_room.GetWired().TryRemove(701));
            var loaded = _room.GetWired().LoadWiredBox(concrete.Item);
            var box = (IWiredConfiguredItem)loaded;
            var original = box.Configuration;
            Assert.True(WiredNativeEditorProjection.TryProject(loaded.Item, box.Descriptor, original, out var native));
            var timing = (loaded as WiredModernTimedTrigger)?.InitialCardTiming;
            var engine = ThreeCardEngine();
            engine.Enqueue(new(WiredEventKind.Speech) { Message = "pending" });
            var pending = engine.ReadStats().Pending;
            await RctSave(store, name, ThreeCardPacket(native));
            Assert.Equal(raw, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701"));
            Assert.Equal(1, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
            Console.WriteLine("RCT_SQL " + JsonSerializer.Serialize(new { name, stage = "equal-schema1", raw }));
            Assert.Same(original, box.Configuration);
            Assert.Equal(timing, (loaded as WiredModernTimedTrigger)?.InitialCardTiming);
            Assert.Equal(pending, engine.ReadStats().Pending);
            await RctSave(store, name, ThreeCardPacket(native with { OwnedIntParams = name == RepeatCard ? [5] : name == CountCard ? [1, 7] : [0] }));
            var changed = store.Load(701, box.Descriptor)!;
            Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
            Assert.Equal(2, changed.Origin!.Native!.Version);
            Console.WriteLine("RCT_SQL " + JsonSerializer.Serialize(new { name, stage = "changed-schema2", raw = db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701") }));
            Assert.Equal(prior.Text, changed.Text);
            Assert.Equal(prior.SelectedItems.ToArray(), changed.SelectedItems.ToArray());
            Assert.Equal(prior.SecondarySelectedItems.ToArray(), changed.SecondarySelectedItems.ToArray());
            Assert.Equal(900, changed.FurniSources["dormant"]);
            Assert.Equal(900, changed.UserSources["dormant"]);
        }
    }

    [Theory]
    [InlineData(1234)]
    [InlineData(100)]
    [InlineData(null)]
    public async Task NativeRepeatCountTeleportDormantMoversRemainSerializedWithoutBecomingExecutable(int? movers)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var raw = RctDormantMoversRow(movers);
        var (loaded, store) = RctFresh(TeleportCard, raw);
        var configured = (IWiredConfiguredItem)loaded;
        var original = configured.Configuration;
        Assert.False(original.FurniSources.ContainsKey("movers"));

        if (movers.HasValue) {
            Assert.Equal(movers, original.Origin!.StoredLegacy!.FurniSources["movers"]);
        }

        Assert.True(WiredNativeEditorProjection.TryProject(loaded.Item, configured.Descriptor, original, out var native));
        var engine = ThreeCardEngine();
        Assert.True(engine.Enqueue(new(WiredEventKind.Speech) { Message = "pending" }));
        var pending = engine.ReadStats().Pending;
        await RctSave(store, TeleportCard, ThreeCardPacket(native));
        Assert.Empty(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(original, configured.Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        Assert.Contains(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        await RctSave(store, TeleportCard, ThreeCardPacket(native with { OwnedIntParams = [1] }));
        var saved = Assert.Single(store.Saves);

        if (movers.HasValue) {
            Assert.Equal(movers, saved.Origin!.Native!.DormantLegacy!.FurniSources["movers"]);
        }

        Assert.True(_room.GetWired().TryGet(701, out var installed));
        RctExecuteDormantMoversTeleport(installed, actor, movers);
        Assert.True(_room.GetWired().TryRemove(701));
        var reloaded = _room.GetWired().LoadWiredBox(loaded.Item);
        RctExecuteDormantMoversTeleport(reloaded, actor, movers);
        var stored = JsonSerializer.Deserialize<WiredNativeEditorConfiguration>(store.Json!)!;

        if (movers.HasValue) {
            Assert.Equal(movers, stored.DormantLegacy!.FurniSources["movers"]);
        }
    }

    [WiredChestDatabaseFact]
    public async Task NativeRepeatCountTeleportActualSqlDormantMoversNoopChangedReloadAndExecution()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");
        var (_, actor) = PrepareSpeech(new SpeechClock());

        var movers = 1234;
        db.Connection.Execute("DELETE FROM wired_item_configurations");
        var raw = RctDormantMoversRow(movers);
        db.Connection.Execute("INSERT INTO wired_item_configurations (item_id,box_name,schema_version,configuration) VALUES (701,@name,1,@raw)", new { name = TeleportCard, raw });
        var (concrete, _) = RctFresh(TeleportCard);
        var store = new WiredConfigurationStore(db.Database);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
        Assert.True(_room.GetWired().TryRemove(701));
        var loaded = _room.GetWired().LoadWiredBox(concrete.Item);
        var configured = (IWiredConfiguredItem)loaded;
        var original = configured.Configuration;
        Assert.False(original.FurniSources.ContainsKey("movers"));
        Assert.Equal(movers, original.Origin!.StoredLegacy!.FurniSources["movers"]);
        Assert.True(WiredNativeEditorProjection.TryProject(loaded.Item, configured.Descriptor, original, out var native));
        var engine = ThreeCardEngine();
        Assert.True(engine.Enqueue(new(WiredEventKind.Speech) { Message = "pending" }));
        var pending = engine.ReadStats().Pending;
        await RctSave(store, TeleportCard, ThreeCardPacket(native));
        Assert.Equal(raw, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701"));
        Assert.Equal(1, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
        Assert.Same(original, configured.Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        await RctSave(store, TeleportCard, ThreeCardPacket(native with { OwnedIntParams = [1] }));
        Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
        var changedRaw = db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701");
        Assert.Equal(movers, JsonSerializer.Deserialize<WiredNativeEditorConfiguration>(changedRaw)!.DormantLegacy!.FurniSources["movers"]);
        Assert.True(_room.GetWired().TryGet(701, out var installed));
        RctExecuteDormantMoversTeleport(installed, actor, movers);
        Assert.True(_room.GetWired().TryRemove(701));
        var reloaded = _room.GetWired().LoadWiredBox(concrete.Item);
        RctExecuteDormantMoversTeleport(reloaded, actor, movers);
        Assert.Equal(changedRaw, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701"));
        Console.WriteLine("RCT_DORMANT_SQL " + JsonSerializer.Serialize(new { movers, originalRaw = raw, changedRaw }));
    }

    private static string RctDormantMoversRow(int? movers) => JsonSerializer.Serialize(new WiredConfiguration
    {
        IntParams = [0, 100, 0],
        SelectedItems = [702],
        Text = "  dormant raw\r\n  ",
        FurniSources = movers.HasValue ? ImmutableDictionary<string, int>.Empty.Add("movers", movers.Value) : ImmutableDictionary<string, int>.Empty
    }, new JsonSerializerOptions { WriteIndented = true });

    private void RctExecuteDormantMoversTeleport(IWiredItem installed, Plus.HabboHotel.Rooms.RoomUser actor, int? movers)
    {
        var target = _room.GetRoomItemHandler().GetItem(702)!;
        target.Definition.Walkable = true;

        if ((target.GetX, target.GetY) != (1, 1)) {
            Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, target, 1, 1, 0, false, false, false));
        }

        Assert.Equal((1, 1), (target.GetX, target.GetY));
        var frame = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Use) { Actor = actor }, frame.Targets, frame.Operations);
        context.Triggering.UserIds.Add(actor.VirtualId);
        WiredAvatarState.For(_room).FreezeUser(actor, 0, cancelOnTeleport: true);
        var executed = false;

        if (_room.UsesV2Movement) {
            _room.RunFastPass(() => executed = ((WiredModernBox)installed).Execute(context));
        }
        else {
            executed = ((WiredModernBox)installed).Execute(context);
        }

        Assert.True(executed);
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(actor.Frozen);
        var config = ((IWiredConfiguredItem)installed).Configuration;
        Assert.False(config.FurniSources.ContainsKey("movers"));

        if (movers.HasValue) {
            Assert.Equal(movers, config.Origin!.Native!.DormantLegacy!.FurniSources["movers"]);
        }
        else {
            Assert.False(config.Origin!.Native!.DormantLegacy!.FurniSources.ContainsKey("movers"));
        }
    }

    // Observe a committed real SQL row before the publisher installs or acknowledges it.
    private sealed class RctDurableStore(IWiredConfigurationStore inner, Action committed) : IWiredConfigurationStore
    {
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => inner.Load(itemId, descriptor);
        public void Reset(IReadOnlyCollection<uint> ids) => inner.Reset(ids);
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration)
        {
            inner.Save(itemId, descriptor, configuration);
            committed();
        }
    }

    // PublishConfigured reports stale proof via its checked persistence exception; the real service converts it to refusal.
    private static bool RctPublisherRefused(Func<bool> save)
    {
        try {
            return save();
        }
        catch (InvalidOperationException error) when (error.Message == "The fresh native card request is no longer current.") {
            return false;
        }
    }

    private IWiredItem RctFactory(IWiredItem concrete)
    {
        Assert.True(_room.GetWired().TryRemove(concrete.Item.Id));
        var factory = _room.GetWired().CreateConfiguredBox(concrete.Item)!;
        _room.GetWired().AddBox(factory);

        return factory;
    }

    private (IWiredItem Box, ThreeCardStore Store) RctFresh(string name, string? raw = null)
    {
        var store = new ThreeCardStore(name, raw);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
        var interaction = name == RepeatCard ? InteractionType.WiredTrigger : name == CountCard ? InteractionType.WiredCondition : InteractionType.WiredEffect;
        var type = name == RepeatCard ? WiredBoxType.TriggerRepeat : name == CountCard ? WiredBoxType.ConditionUserCountInRoom : WiredBoxType.EffectTeleportToFurni;
        var box = Furni(701, interaction, type);
        box.RoomId = RoomId;
        box.Definition.ItemName = box.Definition.InteractionName = name;
        var first = Furni(702, InteractionType.None, WiredBoxType.None);
        var second = Furni(703, InteractionType.None, WiredBoxType.None);
        first.RoomId = second.RoomId = RoomId;
        _room.GetRoomItemHandler().LoadFurniture([box, first, second]);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        _client.Packets.Clear();
        _client.Sent.Clear();

        return (current, store);
    }

    private Task RctSave(IWiredConfigurationStore store, string name, FlashIncomingPacket packet)
    {
        var service = new WiredConfigurationService(store, null!, TestLogging.For<WiredConfigurationService>());

        return name == RepeatCard ? new SaveWiredTriggerConfigEvent(service).Parse(_client, packet)
            : name == CountCard ? new SaveWiredConditionConfigEvent(service).Parse(_client, packet)
            : new SaveWiredEffectConfigEvent(service).Parse(_client, packet);
    }

    private static WiredNativeEditorConfiguration RctNative(string name, bool defaults = false) => new()
    {
        Category = name == RepeatCard ? WiredBoxCategory.Trigger : name == CountCard ? WiredBoxCategory.Condition : WiredBoxCategory.Action,
        NativeCode = name == RepeatCard ? 6 : name == CountCard ? 5 : 8,
        OwnedIntParams = name == RepeatCard ? [defaults ? 1 : 4] : name == CountCard ? (defaults ? [1, 50] : [2, 7]) : [defaults ? 0 : 1],
        PrimaryItems = defaults || name != TeleportCard ? [] : [new(702, false), new(703, false)],
        SecondaryItems = defaults || name != TeleportCard ? [] : [new(703, false)],
        FurniSourceTypes = name == TeleportCard ? [100] : [],
        UserSourceTypes = name == TeleportCard ? [defaults ? 0 : 201] : [],
        Delay = name == TeleportCard ? (defaults ? 0 : 4) : null,
        Quantifier = name == CountCard ? 0 : null
    };

    // Literal Sept9 borrowed footer oracle, independent of production metadata/compile.
    private static void RctReply(byte[] bytes, string name, WiredNativeEditorConfiguration expected)
    {
        var packet = new FlashIncomingPacket { Buffer = bytes };
        int[] Ints()
        {
            var count = packet.ReadInt();
            Assert.InRange(count, 0, 100);

            return Enumerable.Range(0, count).Select(_ => packet.ReadInt()).ToArray();
        }
        Assert.Equal(100, packet.ReadInt());
        Assert.Equal(expected.PrimaryItems.Select(p => p.WireId), Ints());
        Assert.Equal(expected.SecondaryItems.Select(p => p.WireId), Ints());
        Assert.Equal(10, packet.ReadInt());
        Assert.Equal(701, packet.ReadInt());
        Assert.Equal("", packet.ReadString());
        Assert.Equal(expected.OwnedIntParams, Ints());
        Assert.Empty(Ints());
        Assert.Equal(expected.FurniSourceTypes, Ints());
        Assert.Equal(expected.UserSourceTypes, Ints());
        Assert.Equal(expected.NativeCode, packet.ReadInt());

        if (name == TeleportCard) {
            Assert.Equal(expected.Delay, packet.ReadInt());
        }

        if (name == CountCard) {
            Assert.Equal(0, packet.ReadInt());
        }

        Assert.Equal(1, packet.ReadByte());
        Assert.Equal(name == TeleportCard ? 1 : 0, packet.ReadInt());

        if (name == TeleportCard) {
            Assert.Equal(new[] { 0, 100, 200, 201 }, Ints());
        }

        Assert.Equal(name == TeleportCard ? 1 : 0, packet.ReadInt());

        if (name == TeleportCard) {
            Assert.Equal(new[] { 0, 200, 201 }, Ints());
        }

        Assert.Equal(name == TeleportCard ? new[] { 100 } : Array.Empty<int>(), Ints());
        Assert.Equal(name == TeleportCard ? new[] { 0 } : Array.Empty<int>(), Ints());
        Assert.Equal(0, packet.ReadByte());

        if (name == CountCard) {
            Assert.Equal(0, packet.ReadByte());
            Assert.Equal(0, packet.ReadByte());
        }

        Assert.Equal(0, packet.ReadInt());
        Assert.Equal(name == RepeatCard ? new[] { 1 } : name == CountCard ? new[] { 1, 50 } : new[] { 0 }, Ints());
        Assert.False(packet.HasDataRemaining());
    }
}
