using System.Collections.Immutable;
using Dapper;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public void NativeThreeCardsPristineOpenIsPresentationOnly(string name)
    {
        var (original, store) = ThreeCardFresh(name);
        original.Item.Interactor.OnTrigger(_client, original.Item, 0, true);
        var opened = ThreeCardReply(_client.Packets.Single(packet => packet.Header == ThreeCardHeader(name)).Body, name);
        Assert.Equal(ThreeCardDefaults(name), opened.Owned);
        Assert.Empty(store.Saves);
        Assert.True(_room.GetWired().TryGet(701, out var before));
        Assert.Same(original, before);
        _client.Packets.Clear();

    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public async Task NativeThreeCardsActualHandlerSavesAndReopens(string name)
    {
        var (original, store) = ThreeCardFresh(name);
        await ThreeCardSave(store, name, ThreeCardPacket(name));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(name == "wf_trg_walks_on_furni" ? new[] { 201 }
            : name == "wf_act_show_message" ? new[] { 11, 1, 211, 2 } : new[] { 0, 201 }, saved.IntParams);
        Assert.Equal(name == "wf_act_show_message" ? "  hello\r\nworld  " : "", saved.Text);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.NotSame(original, current);
        Assert.Contains(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        _client.Packets.Clear();
        current.Item.Interactor.OnTrigger(_client, current.Item, 0, true);
        var reopened = ThreeCardReply(Assert.Single(_client.Packets).Body, name);
        Assert.Equal(name == "wf_trg_walks_on_furni" ? Array.Empty<int>()
            : name == "wf_act_show_message" ? new[] { 1, 211, 2 } : new[] { 0 }, reopened.Owned);
        Assert.Equal(saved.Text, reopened.Text);
        Assert.Equal(new[] { 702, 703 }, reopened.Primary);
        Assert.Equal(new[] { 703 }, reopened.Secondary);
        Assert.Equal(name == "wf_act_show_message" ? Array.Empty<int>() : new[] { 201 }, reopened.Furni);
        Assert.Equal(name == "wf_act_show_message" ? new[] { 11 } : Array.Empty<int>(), reopened.Users);
        Assert.Equal(name == "wf_act_show_message" ? 4 : (int?)null, reopened.Delay);
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public async Task NativeThreeCardsFirstDefaultSavePersistsBeforePromotionThenIdenticalSaveIsNoop(string name)
    {
        var (original, store) = ThreeCardFresh(name);
        var native = ThreeCardNative(name) with
        {
            OwnedIntParams = ThreeCardDefaults(name).ToImmutableArray(),
            Text = "",
            PrimaryItems = [],
            SecondaryItems = [],
            FurniSourceTypes = name == "wf_act_show_message" ? [] : [100],
            UserSourceTypes = name == "wf_act_show_message" ? [0] : [],
            Delay = name == "wf_act_show_message" ? 0 : null
        };
        store.BeforeSave = () =>
        {
            Assert.True(_room.GetWired().TryGet(701, out var current));
            Assert.Same(original, current);
            Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        };
        await ThreeCardSave(store, name, ThreeCardPacket(native));
        var saved = Assert.Single(store.Saves);
        Assert.NotNull(saved.Origin!.Native);
        Assert.True(_room.GetWired().TryGet(701, out var promoted));
        Assert.NotSame(original, promoted);
        var configuration = Assert.IsAssignableFrom<IWiredConfiguredItem>(promoted).Configuration;
        var raw = store.Json;
        var engine = ThreeCardEngine();
        Assert.True(engine.Enqueue(new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Message = "unrelated" }));
        var pending = engine.ReadStats().Pending;
        Assert.True(pending > 0);
        var published = 0;
        engine.ConfigurationPublished = _ => published++;
        store.BeforeSave = () => throw new InvalidOperationException("No-op must not write.");
        await ThreeCardSave(store, name, ThreeCardPacket(native));
        Assert.Single(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(configuration, ((IWiredConfiguredItem)promoted).Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        Assert.Equal(0, published);
    }

    public static IEnumerable<object[]> ThreeCardProofMutations() =>
        from name in new[] { "wf_trg_walks_on_furni", "wf_act_show_message", "wf_cnd_furnis_hv_avtrs" }
        from change in new[] { "rights", "remove", "replace", "text", "null-text", "bool", "items", "dictionary", "dictionary-pick",
            "definition", "definition-id", "wired-type", "kind", "room", "pose", "pick-definition", "pick-id", "pick-kind", "pick-temporary", "pick-pose" }
        select new object[] { name, change };

    [Theory]
    [MemberData(nameof(ThreeCardProofMutations))]
    public void NativeThreeCardsFinalRightsRechecksWholePristineRequest(string name, string change)
    {
        var (original, store) = ThreeCardFresh(name);
        var wired = _room.GetWired();
        var request = ThreeCardNative(name);
        var proof = wired.CapturePristineCard(original, request, () => true)!;
        Assert.NotNull(proof);
        var candidate = wired.CreateConfiguredBox(original.Item, proof.Descriptor)!;
        Assert.True(WiredNativeEditorProjection.TryCompile(701, proof.Descriptor, request, out var configuration));
        var picked = _room.GetRoomItemHandler().GetItem(702)!;
        IWiredItem? replacement = null;
        var calls = 0;
        var saved = WiredConfigurationSave.TrySave(candidate, configuration, store, out _,
            publish: (_, validated, persist) => wired.PublishPristineCard(proof, candidate, validated, () =>
            {
                calls++;

                switch (change) {
                    case "remove":
                    case "replace":
                        Assert.True(wired.TryRemove(701));

                        if (change == "replace") {
                            replacement = wired.LoadWiredBox(original.Item);
                        }

                        break;
                    case "text":
                        original.StringData = " ";
                        break;
                    case "null-text":
                        original.StringData = null!;
                        break;
                    case "bool":
                        original.BoolData = true;
                        break;
                    case "items":
                        original.ItemsData = "raw";
                        break;
                    case "dictionary":
                        original.SetItems = new();
                        break;
                    case "dictionary-pick":
                        original.SetItems[702] = picked;
                        break;
                    case "definition":
                        original.Item.Definition = new();
                        break;
                    case "definition-id":
                        original.Item.Definition.Id++;
                        break;
                    case "wired-type":
                        original.Item.Definition.WiredType = WiredBoxType.None;
                        break;
                    case "kind":
                        original.Item.Definition.Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Wall;
                        break;
                    case "room":
                        original.Item.RoomId++;
                        break;
                    case "pose":
                        original.Item.SetState(1, 1, 0, new());
                        break;
                    case "pick-definition":
                        picked.Definition = new();
                        break;
                    case "pick-id":
                        picked.Id++;
                        break;
                    case "pick-kind":
                        picked.Definition.Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Wall;
                        break;
                    case "pick-temporary":
                        typeof(Item).GetProperty(nameof(Item.IsTemporary))!.SetValue(picked, true);
                        break;
                    case "pick-pose":
                        picked.SetState(2, 1, 0, new());
                        break;
                }

                return change != "rights";
            }, persist));
        Assert.False(saved);
        Assert.Empty(store.Saves);
        Assert.Equal(1, calls);
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);

        if (change is "remove" or "replace") {
            Assert.Equal(change == "replace", wired.TryGet(701, out var current));
            Assert.Same(replacement, current);
        }
    }

    public static IEnumerable<object[]> ThreeCardDirectCoordinates() =>
        from name in new[] { "wf_trg_walks_on_furni", "wf_act_show_message", "wf_cnd_furnis_hv_avtrs" }
        from picked in new[] { false, true }
        from axis in new[] { "x", "y", "z", "z-bits", "unchanged" }
        select new object[] { name, picked, axis };

    [Theory]
    [MemberData(nameof(ThreeCardDirectCoordinates))]
    public void NativeThreeCardsActualSaveFinalRightsRejectsDirectCoordinateMutation(string name, bool picked, string axis)
    {
        var (original, store) = ThreeCardFresh(name);
        var wired = _room.GetWired();
        var request = ThreeCardNative(name);
        var proof = wired.CapturePristineCard(original, request, () => true)!;
        Assert.NotNull(proof);
        var candidate = wired.CreateConfiguredBox(original.Item, proof.Descriptor)!;
        Assert.True(WiredNativeEditorProjection.TryCompile(701, proof.Descriptor, request, out var configuration));
        var item = picked ? _room.GetRoomItemHandler().GetItem(702)! : original.Item;
        var generation = item.MovementGeneration;
        var placement = item.Placement;
        var x = item.GetX;
        var y = item.GetY;
        var z = item.GetZ;
        var calls = 0;
        var saved = WiredConfigurationSave.TrySave(candidate, configuration, store, out _,
            publish: (_, validated, persist) => wired.PublishPristineCard(proof, candidate, validated, () =>
            {
                calls++;

                switch (axis) {
                    case "x":
                        item.GetX++;
                        break;
                    case "y":
                        item.GetY++;
                        break;
                    case "z":
                        item.GetZ++;
                        break;
                    case "z-bits":
                        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(item.GetZ));
                        item.GetZ = BitConverter.Int64BitsToDouble(long.MinValue);
                        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(item.GetZ));
                        break;
                    case "unchanged":
                        item.GetX = item.GetX;
                        item.GetY = item.GetY;
                        item.GetZ = item.GetZ;
                        break;
                }

                return true;
            }, persist));
        Assert.Equal(generation, item.MovementGeneration);
        Assert.Equal(placement, item.Placement);
        Assert.Equal(x + (axis == "x" ? 1 : 0), item.GetX);
        Assert.Equal(y + (axis == "y" ? 1 : 0), item.GetY);
        Assert.Equal(axis == "z-bits" ? long.MinValue : BitConverter.DoubleToInt64Bits(z + (axis == "z" ? 1 : 0)),
            BitConverter.DoubleToInt64Bits(item.GetZ));
        Assert.Equal(1, calls);
        Assert.Same(item, _room.GetRoomItemHandler().GetItem(item.Id));
        Assert.Same(_room, item.GetRoom());
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        Assert.Equal(axis == "unchanged", saved);
        Assert.True(wired.TryGet(701, out var registered));

        if (axis == "unchanged") {
            Assert.Single(store.Saves);
            Assert.Same(candidate, registered);
        }
        else {
            Assert.Empty(store.Saves);
            Assert.Same(original, registered);
        }
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public async Task NativeThreeCardsActualStoredV1EqualSavePreservesRawDormantDataAndPending(string name)
    {
        var prior = new WiredConfiguration
        {
            IntParams = name == "wf_trg_walks_on_furni" ? [201] : name == "wf_act_show_message" ? [11, 1, 211] : [0, 201],
            Text = name == "wf_act_show_message" ? "  hello\r\nworld  " : "dormant raw text",
            Delay = name == "wf_act_show_message" ? 4 : 0,
            SelectedItems = [702, 703],
            SecondarySelectedItems = [703],
            FurniSources = name == "wf_act_show_message" ? ImmutableDictionary<string, int>.Empty.Add("dormant", 900)
                : ImmutableDictionary<string, int>.Empty.Add("items", 201).Add("dormant", 900),
            UserSources = name == "wf_act_show_message" ? ImmutableDictionary<string, int>.Empty.Add("users", 11).Add("dormant", 900)
                : ImmutableDictionary<string, int>.Empty.Add("dormant", 900),
            Snapshots = [new(702, 0, 1, 1, 0, 0, "state")]
        };
        var raw = "  " + JsonSerializer.Serialize(prior) + "\n";
        var (loaded, store) = ThreeCardFresh(name, raw);
        var box = Assert.IsAssignableFrom<IWiredConfiguredItem>(loaded);
        var before = box.Configuration;
        loaded.Item.Interactor.OnTrigger(_client, loaded.Item, 0, true);
        ThreeCardReply(Assert.Single(_client.Packets).Body, name);
        var engine = ThreeCardEngine();
        Assert.True(engine.Enqueue(new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Message = "unrelated" }));
        var pending = engine.ReadStats().Pending;
        await ThreeCardSave(store, name, ThreeCardPacket(ThreeCardNative(name) with
        { OwnedIntParams = name == "wf_act_show_message" ? [1, 211, -1] : ThreeCardNative(name).OwnedIntParams }));
        Assert.Contains(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        Assert.Empty(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(before, box.Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        var changed = ThreeCardNative(name) with
        {
            OwnedIntParams = name == "wf_trg_walks_on_furni" ? [] : name == "wf_act_show_message" ? [0, 211, 2] : [1],
            FurniSourceTypes = name == "wf_act_show_message" ? [] : [200]
        };
        await ThreeCardSave(store, name, ThreeCardPacket(changed));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(prior.Text, saved.Text);
        Assert.Equal(900, saved.FurniSources["dormant"]);
        Assert.Equal(900, saved.UserSources["dormant"]);
        Assert.Equal(prior.Snapshots.ToArray(), saved.Snapshots.ToArray());
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public async Task NativeThreeCardsNonpristineConcreteRefusesEditingWithoutChangingLegacyExecutionState(string name)
    {
        var (box, store) = ThreeCardFresh(name);
        box.StringData = " ";
        box.BoolData = true;
        box.ItemsData = "inactive bytes";
        box.SetItems[702] = _room.GetRoomItemHandler().GetItem(702)!;
        var picks = box.SetItems;
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        await ThreeCardSave(store, name, ThreeCardPacket(name));
        Assert.Empty(store.Saves);
        Assert.DoesNotContain(ThreeCardHeader(name), _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.Same(box, current);
        Assert.Equal((" ", true, "inactive bytes"), (box.StringData, box.BoolData, box.ItemsData));
        Assert.Same(picks, box.SetItems);
    }

    [Theory]
    [InlineData(200, 8, true)]
    [InlineData(201, 8, false)]
    [InlineData(200, 9, false)]
    public async Task NativeThreeCardsShowTextBoundsRefuseWithoutTruncating(int length, int lines, bool valid)
    {
        var (_, store) = ThreeCardFresh("wf_act_show_message");
        var text = new string('x', length - (lines - 1) * 2) + string.Concat(Enumerable.Repeat("\r\n", lines - 1));
        await ThreeCardSave(store, "wf_act_show_message", ThreeCardPacket(ThreeCardNative("wf_act_show_message") with { Text = text }));
        Assert.Equal(valid ? 1 : 0, store.Saves.Count);

        if (valid) {
            Assert.Equal(text, store.Saves[0].Text);
        }
    }

    [WiredChestDatabaseFact]
    public async Task NativeThreeCardsActualSqlRollbackThenSaveReloadsAllThreeCategories()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");

        foreach (var name in new[] { "wf_trg_walks_on_furni", "wf_act_show_message", "wf_cnd_furnis_hv_avtrs" }) {
            db.Connection.Execute("DELETE FROM wired_item_configurations");
            var (original, _) = ThreeCardFresh(name);
            var store = new WiredConfigurationStore(db.Database);
            db.Connection.Execute("CREATE TRIGGER reject_native_three BEFORE INSERT ON wired_item_configurations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced three-card failure'");
            await ThreeCardSave(store, name, ThreeCardPacket(name));
            Assert.Equal(0, db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_item_configurations"));
            Assert.True(_room.GetWired().TryGet(701, out var refused));
            Assert.Same(original, refused);
            Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
            db.Connection.Execute("DROP TRIGGER reject_native_three");
            await ThreeCardSave(store, name, ThreeCardPacket(name));
            Assert.True(_room.GetWired().TryGet(701, out var saved));
            var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(saved);
            Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
            var reloaded = store.Load(701, configured.Descriptor)!;
            Assert.True(WiredNativeEditorProjection.IsBound(701, configured.Descriptor, reloaded));
            Assert.True(WiredNativeEditorProjection.Matches(configured.Configuration, reloaded));
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
            Assert.True(_room.GetWired().TryRemove(701));
            var current = Assert.IsAssignableFrom<IWiredConfiguredItem>(_room.GetWired().LoadWiredBox(original.Item));
            _client.Packets.Clear();
            current.Item.Interactor.OnTrigger(_client, current.Item, 0, true);
            ThreeCardReply(Assert.Single(_client.Packets).Body, name);
        }
    }

    private WiredStackEngine ThreeCardEngine() => (WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
        .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetWired())!;

    private static WiredNativeEditorConfiguration ThreeCardNative(string name) => new()
    {
        Category = name == "wf_trg_walks_on_furni" ? WiredBoxCategory.Trigger : name == "wf_act_show_message" ? WiredBoxCategory.Action : WiredBoxCategory.Condition,
        NativeCode = name == "wf_act_show_message" ? 7 : 1,
        OwnedIntParams = name == "wf_trg_walks_on_furni" ? [] : name == "wf_act_show_message" ? [1, 211, 2] : [0],
        Text = name == "wf_act_show_message" ? "  hello\r\nworld  " : "",
        PrimaryItems = [new(702, false), new(703, false)],
        SecondaryItems = [new(703, false)],
        FurniSourceTypes = name == "wf_act_show_message" ? [] : [201],
        UserSourceTypes = name == "wf_act_show_message" ? [11] : [],
        Delay = name == "wf_act_show_message" ? 4 : null,
        Quantifier = name == "wf_cnd_furnis_hv_avtrs" ? 0 : null
    };

    private static Plus.Communication.Flash.FlashIncomingPacket ThreeCardPacket(WiredNativeEditorConfiguration native, int itemId = 701)
    {
        var values = new List<object> { itemId, native.OwnedIntParams.Length };
        values.AddRange(native.OwnedIntParams.Cast<object>());
        values.Add(native.Text);
        void Items(ImmutableArray<WiredNativeItemReference> items)
        {
            values.Add(items.Length);
            values.AddRange(items.Select(item => (object)item.WireId));
        }
        void Ints(ImmutableArray<int> ints)
        {
            values.Add(ints.Length);
            values.AddRange(ints.Cast<object>());
        }
        Items(native.PrimaryItems);

        if (native.Category == WiredBoxCategory.Action) {
            values.Add(native.Delay!.Value);
        }

        if (native.Category == WiredBoxCategory.Condition) {
            values.Add(native.Quantifier!.Value);
        }

        Ints(native.FurniSourceTypes);
        Ints(native.UserSourceTypes);
        values.Add(native.VariableIds.Length);
        values.AddRange(native.VariableIds.Cast<object>());
        Items(native.SecondaryItems);

        return ClientPacket(values.ToArray());
    }

    [Fact]
    public async Task NativeThreeCardsShowCompleteStylesWidthsVisibilityAndSourcesReachActualHandler()
    {
        var (_, store) = ThreeCardFresh("wf_act_show_message");
        var styles = new[] { 34, 200, 201, 202, 210, 211, 212, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 250, 251, 252 };
        var count = 0;

        foreach (var style in styles) {
            foreach (var width in new[] { -1, 0, 1, 2 }) {
                foreach (var visibility in new[] { 0, 1 }) {
                    foreach (var source in new[] { 0, 11, 200, 201 }) {
                        var native = ThreeCardNative("wf_act_show_message") with { OwnedIntParams = [visibility, style, width], UserSourceTypes = [source] };
                        await ThreeCardSave(store, "wf_act_show_message", ThreeCardPacket(native));
                        Assert.True(_room.GetWired().TryGet(701, out var current));
                        Assert.Equal(new[] { source, visibility, style, width }, ((IWiredConfiguredItem)current).Configuration.IntParams);
                        count++;
                    }
                }
            }
        }

        Assert.Equal(640, count);
        Assert.Equal(640, store.Saves.Count);
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public async Task NativeThreeCardsAllFloorSourcesAndRequireAllRemainSeparateFromCategoryFields(string name)
    {
        var (_, store) = ThreeCardFresh(name);

        foreach (var source in new[] { 0, 100, 200, 201 }) {
            foreach (var requireAll in new[] { 0, 1 }) {
                await ThreeCardSave(store, name, ThreeCardPacket(ThreeCardNative(name) with
                { OwnedIntParams = name == "wf_trg_walks_on_furni" ? [] : [requireAll], FurniSourceTypes = [source] }));
                Assert.True(_room.GetWired().TryGet(701, out var current));
                Assert.Equal(name == "wf_trg_walks_on_furni" ? new[] { source } : new[] { requireAll, source },
                    ((IWiredConfiguredItem)current).Configuration.IntParams);
            }
        }
    }

    public static IEnumerable<object[]> ThreeCardInvalidInputs() =>
        from name in new[] { "wf_trg_walks_on_furni", "wf_act_show_message", "wf_cnd_furnis_hv_avtrs" }
        from change in new[] { "owned-count", "source-count", "source", "variables", "wall", "missing-pick", "duplicate", "quantifier-or-visibility", "text-or-style", "width-or-text" }
        select new object[] { name, change };

    [Theory]
    [MemberData(nameof(ThreeCardInvalidInputs))]
    public async Task NativeThreeCardsMalformedNativeRefusesWithoutPromotionOrWrite(string name, string change)
    {
        var (original, store) = ThreeCardFresh(name);
        var native = ThreeCardNative(name);
        native = change switch
        {
            "owned-count" => native with { OwnedIntParams = [9, 9, 9, 9] },
            "source-count" => native with { FurniSourceTypes = [], UserSourceTypes = [] },
            "source" => name == "wf_act_show_message" ? native with { UserSourceTypes = [10] } : native with { FurniSourceTypes = [110] },
            "variables" => native with { VariableIds = ["123"] },
            "wall" => native with { PrimaryItems = [new(702, true)] },
            "missing-pick" => native with { SecondaryItems = [new(999, false)] },
            "duplicate" => native with { PrimaryItems = [new(702, false), new(702, false)] },
            "quantifier-or-visibility" => name == "wf_cnd_furnis_hv_avtrs" ? native with { Quantifier = 1 }
                : name == "wf_act_show_message" ? native with { OwnedIntParams = [2, 211, 2] } : native with { OwnedIntParams = [1] },
            "text-or-style" => name == "wf_act_show_message" ? native with { OwnedIntParams = [1, 33, 2] } : native with { Text = "invalid" },
            _ => name == "wf_act_show_message" ? native with { OwnedIntParams = [1, 211, 3] } : native with { Text = "invalid" }
        };
        await ThreeCardSave(store, name, ThreeCardPacket(native));
        Assert.Empty(store.Saves);
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.Same(original, current);
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public async Task NativeThreeCardsRejectTrailingAndTruncatedPacketsWithoutServiceEffects(string name)
    {
        var (original, store) = ThreeCardFresh(name);
        var body = ThreeCardPacket(name).Buffer.ToArray();
        await ThreeCardSave(store, name, new() { Buffer = body.Concat(new byte[] { 0 }).ToArray() });
        await ThreeCardSave(store, name, new() { Buffer = body[..^1] });
        Assert.Empty(store.Saves);
        Assert.Empty(_client.Packets);
        Assert.True(_room.GetWired().TryGet(701, out var current));
        Assert.Same(original, current);
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public void NativeThreeCardsMissingOrAlteredOriginCannotValidateApplyOrProject(string name)
    {
        var (original, _) = ThreeCardFresh(name);
        var descriptor = original.Item.Definition.WiredDescriptor!;
        var candidate = _room.GetWired().CreateConfiguredBox(original.Item, descriptor)!;
        Assert.False(WiredNativeEditorProjection.TryProject(original.Item, descriptor, candidate.Configuration, out _));
        Assert.True(WiredNativeEditorProjection.TryCompile(701, descriptor, ThreeCardNative(name), out var native));

        foreach (var invalid in new[] { new WiredConfiguration { IntParams = native.IntParams, Text = native.Text },
            native with { Text = "rebound" }, native.Bind(native.Origin! with { ItemId = 702 }), native with { Version = 2 } }) {
            Assert.False(candidate.TryValidateConfiguration(invalid, out _, out _));
            Assert.Throws<InvalidDataException>(() => candidate.ApplyConfiguration(invalid));
            Assert.False(WiredNativeEditorProjection.TryProject(original.Item, descriptor, invalid, out _));
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("style")]
    [InlineData("length")]
    [InlineData("shape")]
    [InlineData("quota")]
    [InlineData("variables")]
    public async Task NativeThreeCardsUnrepresentableShowStoredV1RetainsDataAndRuntime(string change)
    {
        var prior = new WiredConfiguration { IntParams = [11, 1, 211, -1], Text = "legacy message", Delay = 4 };
        prior = change switch
        {
            "source" => prior with { IntParams = [10, 1, 211, -1] },
            "style" => prior with { IntParams = [11, 1, 0, -1] },
            "length" => prior with { Text = new string('x', 201) },
            "shape" => prior with { SelectionCode = 1 },
            "quota" => prior with { ScoreQuotaPerGame = 1 },
            _ => prior with { VariableIds = ["123"] }
        };
        var raw = JsonSerializer.Serialize(prior);
        var (loaded, store) = ThreeCardFresh("wf_act_show_message", raw);
        var box = Assert.IsAssignableFrom<IWiredConfiguredItem>(loaded);
        var before = box.Configuration;
        Assert.True(box.TryValidateConfiguration(before, out _, out _));
        loaded.Item.Interactor.OnTrigger(_client, loaded.Item, 0, true);
        await ThreeCardSave(store, "wf_act_show_message", ThreeCardPacket("wf_act_show_message"));
        Assert.DoesNotContain(ServerPacketHeader.WiredEffectConfigComposer, _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
        Assert.Empty(store.Saves);
        Assert.Equal(raw, store.Json);
        Assert.Same(before, box.Configuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeThreeCardsWalkRuntimeMatchesOnlyRequestedEventFurniture(bool match)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (_, store) = ThreeCardFresh("wf_trg_walks_on_furni");
        await ThreeCardSave(store, "wf_trg_walks_on_furni", ThreeCardPacket(ThreeCardNative("wf_trg_walks_on_furni") with
        { FurniSourceTypes = [100], PrimaryItems = [new(702, false)] }));
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var frame = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext(_room,
            new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.WalkOn)
            { Actor = actor, EventItem = _room.GetRoomItemHandler().GetItem(match ? 702u : 703u) }, frame.Targets, frame.Operations);
        Assert.Equal(match, ((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox)box).Execute(context));
    }

    [Theory]
    [InlineData(0, ServerPacketHeader.WhisperComposer)]
    [InlineData(1, ServerPacketHeader.ChatComposer)]
    public async Task NativeThreeCardsShowRuntimeRetainsMacrosVisibilityStyleWidthAndWhitespace(int visibility, uint header)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (_, store) = ThreeCardFresh("wf_act_show_message");
        await ThreeCardSave(store, "wf_act_show_message", ThreeCardPacket(ThreeCardNative("wf_act_show_message") with
        { OwnedIntParams = [visibility, 211, 2], UserSourceTypes = [0], Delay = 0, Text = "  %USERNAME%\r\n%ROOMNAME%  " }));
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var frame = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var context = new Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext(_room,
            new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Actor = actor }, frame.Targets, frame.Operations);
        context.Triggering.UserIds.Add(actor.VirtualId);
        _client.Packets.Clear();
        Assert.True(((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox)box).Execute(context));
        var message = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        Assert.Equal(header, _client.Packets[0].Header);
        Assert.Equal(actor.VirtualId, message.ReadInt());
        Assert.Equal("  owner\r\n" + _room.Name + "  ", message.ReadString());
        message.ReadInt();
        Assert.Equal(211, message.ReadInt());
        message.ReadInt();
        message.ReadInt();
        Assert.Equal(2, message.ReadInt());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task NativeThreeCardsConditionUsesExistingAnyAllFootprintAndBotPredicate(int requireAll, bool expected)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (_, store) = ThreeCardFresh("wf_cnd_furnis_hv_avtrs");
        var first = _room.GetRoomItemHandler().GetItem(702)!;
        var second = _room.GetRoomItemHandler().GetItem(703)!;
        first.SetState(1, 1, 0, new());
        second.SetState(3, 3, 0, new());
        actor.X = 1;
        actor.Y = 1;
        await ThreeCardSave(store, "wf_cnd_furnis_hv_avtrs", ThreeCardPacket(ThreeCardNative("wf_cnd_furnis_hv_avtrs") with
        { OwnedIntParams = [requireAll], FurniSourceTypes = [100] }));
        Assert.True(_room.GetWired().TryGet(701, out var box));
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        Assert.Equal(expected, ((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox)box).Execute(context));
        actor.BotData = (Plus.HabboHotel.Rooms.AI.RoomBot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plus.HabboHotel.Rooms.AI.RoomBot));
        Assert.Equal(expected, ((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox)box).Execute(context));
    }

    [Fact]
    public async Task NativeThreeCardsLegacyEmptyConditionAndModernEmptyConditionRemainExplicitlyDifferent()
    {
        var (legacy, store) = ThreeCardFresh("wf_cnd_furnis_hv_avtrs");
        Assert.True(legacy.Execute());
        legacy.SetItems[999] = Furni(999, InteractionType.None, WiredBoxType.None);
        Assert.True(legacy.Execute());
        legacy.SetItems.Clear();
        await ThreeCardSave(store, "wf_cnd_furnis_hv_avtrs", ThreeCardPacket(ThreeCardNative("wf_cnd_furnis_hv_avtrs") with
        { PrimaryItems = [], SecondaryItems = [], FurniSourceTypes = [100] }));
        Assert.True(_room.GetWired().TryGet(701, out var modern));
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        Assert.False(((Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox)modern).Execute(context));
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_act_show_message")]
    [InlineData("wf_cnd_furnis_hv_avtrs")]
    public void NativeThreeCardsPristineProofCannotPublishAnEqualDifferentRequestOrAfterCaptureMutation(string name)
    {
        var (box, _) = ThreeCardFresh(name);
        var wired = _room.GetWired();
        var request = ThreeCardNative(name);
        var proof = wired.CapturePristineCard(box, request, () => true)!;
        Assert.NotNull(proof);
        var candidate = wired.CreateConfiguredBox(box.Item, proof.Descriptor)!;
        Assert.True(WiredNativeEditorProjection.TryCompile(701, proof.Descriptor, request with { }, out var valid));
        Assert.False(wired.PublishPristineCard(proof, candidate, valid, () => true,
            () => throw new InvalidOperationException("Different request must not persist.")));
        var calls = 0;
        Assert.Null(wired.CapturePristineCard(box, request, () =>
        {
            if (++calls == 2) {
                box.SetItems = new();
            }

            return true;
        }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NativeThreeCardsActualWalkCallbackFiresPromotedTriggerAndShowStackOnlyForMatchingPick()
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (walk, walkStore) = ThreeCardFresh("wf_trg_walks_on_furni");
        walk.Item.SetState(1, 1, 0, new());
        await ThreeCardSave(walkStore, "wf_trg_walks_on_furni", ThreeCardPacket(ThreeCardNative("wf_trg_walks_on_furni") with
        { PrimaryItems = [new(702, false)], SecondaryItems = [], FurniSourceTypes = [100] }));
        var showStore = new ThreeCardStore("wf_act_show_message", null);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), showStore);
        var show = Furni(710, InteractionType.WiredEffect, WiredBoxType.EffectShowMessage);
        show.Definition.InteractionName = show.Definition.ItemName = "wf_act_show_message";
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, show, 1, 1, 0, true, false, false));
        Assert.NotNull(_room.GetWired().LoadWiredBox(show));
        await ThreeCardSave(showStore, "wf_act_show_message", ThreeCardPacket(ThreeCardNative("wf_act_show_message") with
        { UserSourceTypes = [0], Delay = 0, OwnedIntParams = [0, 211, -1], Text = "walked", PrimaryItems = [], SecondaryItems = [] }, 710));
        Assert.Same(_room, _room.GetRoomItemHandler().GetItem(702)!.GetRoom());
        Assert.Same(actor, _room.GetRoomUserManager().GetRoomUserByHabbo(_client.GetHabbo().Id));
        Assert.Equal((walk.Item.GetX, walk.Item.GetY), (show.GetX, show.GetY));
        _client.Packets.Clear();
        _room.GetRoomItemHandler().GetItem(703)!.UserWalksOnFurni(actor);
        _room.GetWired().OnCycle();
        _room.GetWired().OnCycle(); // Non-speech actions resume one cycle after their event evaluation.
        Assert.Empty(SpeechMessages(ServerPacketHeader.WhisperComposer));
        _room.GetRoomItemHandler().GetItem(702)!.UserWalksOnFurni(actor);
        _room.GetWired().OnCycle();
        _room.GetWired().OnCycle();
        Assert.Equal(new[] { "walked" }, SpeechMessages(ServerPacketHeader.WhisperComposer));
    }

    [WiredChestDatabaseFact]
    public async Task NativeThreeCardsActualSqlStoredV1RawNoopThenChangedSavePreservesDormantSnapshotsAndMaps()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");

        foreach (var name in new[] { "wf_trg_walks_on_furni", "wf_act_show_message", "wf_cnd_furnis_hv_avtrs" }) {
            db.Connection.Execute("DELETE FROM wired_item_configurations");
            var (fresh, _) = ThreeCardFresh(name);
            var prior = new WiredConfiguration
            {
                IntParams = name == "wf_trg_walks_on_furni" ? [201] : name == "wf_act_show_message" ? [11, 1, 211, 2] : [0, 201],
                Text = name == "wf_act_show_message" ? "  hello\r\nworld  " : "dormant raw bytes",
                Delay = name == "wf_act_show_message" ? 4 : 0,
                SelectedItems = [702, 703],
                SecondarySelectedItems = [703],
                FurniSources = ImmutableDictionary<string, int>.Empty.Add("inactive", 900),
                UserSources = ImmutableDictionary<string, int>.Empty.Add("inactive", 900),
                Snapshots = [new(702, 0, 1, 1, 0, 0, "state")]
            };
            var raw = " \n" + JsonSerializer.Serialize(prior) + "\n ";
            db.Connection.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES(701,@Name,1,@Json)", new { Name = name, Json = raw });
            var store = new WiredConfigurationStore(db.Database);
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
            Assert.True(_room.GetWired().TryRemove(701));
            var loaded = Assert.IsAssignableFrom<IWiredConfiguredItem>(_room.GetWired().LoadWiredBox(fresh.Item));
            var before = loaded.Configuration;
            var engine = ThreeCardEngine();
            Assert.True(engine.Enqueue(new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Message = "unrelated" }));
            var pending = engine.ReadStats().Pending;
            _client.Packets.Clear();
            await ThreeCardSave(store, name, ThreeCardPacket(name));
            Assert.Contains(ServerPacketHeader.HideWiredConfigComposer, _client.Sent);
            Assert.Equal(raw, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=701"));
            Assert.Equal(1, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
            Assert.Same(before, loaded.Configuration);
            Assert.Equal(pending, engine.ReadStats().Pending);
            var changed = ThreeCardNative(name) with
            {
                Text = name == "wf_act_show_message" ? "changed raw\r\ntext" : "",
                PrimaryItems = [new(703, false), new(702, false)],
                OwnedIntParams = name == "wf_trg_walks_on_furni" ? [] : name == "wf_act_show_message" ? [0, 211, 2] : [1]
            };
            await ThreeCardSave(store, name, ThreeCardPacket(changed));
            Assert.Equal(2, db.Connection.QuerySingle<int>("SELECT schema_version FROM wired_item_configurations WHERE item_id=701"));
            Assert.True(_room.GetWired().TryGet(701, out var current));
            var configuration = ((IWiredConfiguredItem)current).Configuration;
            Assert.Equal(new uint[] { 703, 702 }, configuration.SelectedItems);
            Assert.Equal(name == "wf_act_show_message" ? changed.Text : prior.Text, configuration.Text);
            Assert.Equal(900, configuration.FurniSources["inactive"]);
            Assert.Equal(900, configuration.UserSources["inactive"]);
            Assert.Equal(prior.Snapshots.ToArray(), configuration.Snapshots.ToArray());
            Assert.True(WiredNativeEditorProjection.Matches(configuration, store.Load(701, loaded.Descriptor)!));
        }
    }

    private (IWiredItem Box, ThreeCardStore Store) ThreeCardFresh(string name, string? json = null)
    {
        var store = new ThreeCardStore(name, json);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
        var interaction = name == "wf_trg_walks_on_furni" ? InteractionType.WiredTrigger
            : name == "wf_act_show_message" ? InteractionType.WiredEffect : InteractionType.WiredCondition;
        var type = name == "wf_trg_walks_on_furni" ? WiredBoxType.TriggerWalkOnFurni
            : name == "wf_act_show_message" ? WiredBoxType.EffectShowMessage : WiredBoxType.ConditionFurniHasUsers;
        var item = Furni(701, interaction, type);
        item.RoomId = RoomId;
        item.Definition.ItemName = name;
        item.Definition.InteractionName = name;
        var first = Furni(702, InteractionType.None, WiredBoxType.None);
        var second = Furni(703, InteractionType.None, WiredBoxType.None);
        first.RoomId = second.RoomId = RoomId;
        _room.GetRoomItemHandler().LoadFurniture([item, first, second]);
        Assert.True(_room.GetWired().TryGet(701, out var box));
        _client.Packets.Clear();
        _client.Sent.Clear();

        return (box, store);
    }

    private Task ThreeCardSave(IWiredConfigurationStore store, string name, Plus.Communication.Flash.FlashIncomingPacket packet)
    {
        var service = new WiredConfigurationService(store, null!, TestLogging.For<WiredConfigurationService>());

        return name switch
        {
            "wf_trg_walks_on_furni" => new SaveWiredTriggerConfigEvent(service).Parse(_client, packet),
            "wf_act_show_message" => new SaveWiredEffectConfigEvent(service).Parse(_client, packet),
            _ => new SaveWiredConditionConfigEvent(service).Parse(_client, packet)
        };
    }

    private static Plus.Communication.Flash.FlashIncomingPacket ThreeCardPacket(string name)
    {
        var values = new List<object> { 701 };
        var owned = name == "wf_trg_walks_on_furni" ? Array.Empty<int>()
            : name == "wf_act_show_message" ? new[] { 1, 211, 2 } : new[] { 0 };
        values.Add(owned.Length);
        values.AddRange(owned.Cast<object>());
        values.Add(name == "wf_act_show_message" ? "  hello\r\nworld  " : "");
        values.AddRange(new object[] { 2, 702, 703 });

        if (name == "wf_act_show_message") {
            values.Add(4);
        }

        if (name == "wf_cnd_furnis_hv_avtrs") {
            values.Add(0);
        }

        values.AddRange(name == "wf_act_show_message" ? new object[] { 0, 1, 11 } : new object[] { 1, 201, 0 });
        values.AddRange(new object[] { 0, 1, 703 });

        return ClientPacket(values.ToArray());
    }

    private static int[] ThreeCardDefaults(string name) => name == "wf_trg_walks_on_furni" ? []
        : name == "wf_act_show_message" ? [0, 34, -1] : [1];

    private static uint ThreeCardHeader(string name) => name == "wf_trg_walks_on_furni" ? ServerPacketHeader.WiredTriggeRconfigComposer
        : name == "wf_act_show_message" ? ServerPacketHeader.WiredEffectConfigComposer : ServerPacketHeader.WiredConditionConfigComposer;

    private static ThreeCardFields ThreeCardReply(byte[] bytes, string name)
    {
        var packet = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = bytes };
        Assert.Equal(WiredConfigurationLimits.SelectedItems, packet.ReadInt());
        int[] Ints()
        {
            var count = packet.ReadInt();
            Assert.InRange(count, 0, 100);

            return Enumerable.Range(0, count).Select(_ => packet.ReadInt()).ToArray();
        }
        var primary = Ints();
        var secondary = Ints();
        Assert.Equal(10, packet.ReadInt());
        Assert.Equal(701, packet.ReadInt());
        var text = packet.ReadString();
        var owned = Ints();
        Assert.Equal(0, packet.ReadInt());
        var furni = Ints();
        var users = Ints();
        Assert.Equal(name == "wf_act_show_message" ? 7 : 1, packet.ReadInt());
        int? delay = name == "wf_act_show_message" ? packet.ReadInt() : null;

        if (name == "wf_cnd_furnis_hv_avtrs") {
            Assert.Equal(0, packet.ReadInt());
        }

        Assert.Equal(1, packet.ReadByte());
        Assert.Equal(name == "wf_act_show_message" ? 0 : 1, packet.ReadInt());

        if (name != "wf_act_show_message") {
            Assert.Equal(new[] { 0, 100, 200, 201 }, Ints());
        }

        Assert.Equal(name == "wf_act_show_message" ? 1 : 0, packet.ReadInt());

        if (name == "wf_act_show_message") {
            Assert.Equal(new[] { 0, 11, 200, 201 }, Ints());
        }

        Assert.Equal(name == "wf_act_show_message" ? Array.Empty<int>() : new[] { 100 }, Ints());
        Assert.Equal(name == "wf_act_show_message" ? new[] { 0 } : Array.Empty<int>(), Ints());
        Assert.Equal(0, packet.ReadByte());

        if (name == "wf_cnd_furnis_hv_avtrs") {
            Assert.Equal(0, packet.ReadByte());
            Assert.Equal(0, packet.ReadByte());
        }

        Assert.Equal(0, packet.ReadInt());
        Assert.Equal(ThreeCardDefaults(name), Ints());
        Assert.False(packet.HasDataRemaining());

        return new(primary, secondary, text, owned, furni, users, delay);
    }

    private sealed record ThreeCardFields(int[] Primary, int[] Secondary, string Text, int[] Owned, int[] Furni, int[] Users, int? Delay);

    private sealed class ThreeCardStore(string name, string? json) : IWiredConfigurationStore
    {
        public List<WiredConfiguration> Saves { get; } = [];
        public string? Json { get; private set; } = json;
        public Action? BeforeSave { get; set; }
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => Json == null ? null
            : new WiredConfigurationStore(new ModernWiredRuntimeTests.StoredRuntimeRowsDatabase([new(itemId, name,
                JsonDocument.Parse(Json).RootElement.GetProperty("Version").GetInt32(), Json)])).Load(itemId, descriptor);
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration)
        {
            BeforeSave?.Invoke();
            Saves.Add(configuration);
            Json = JsonSerializer.Serialize(configuration.Origin!.Native!);
        }
        public void Reset(IReadOnlyCollection<uint> itemIds) => throw new InvalidOperationException("Unexpected reset.");
    }
}
