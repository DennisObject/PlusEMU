using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Conditions;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public class WiredModernContractsTests
{
    [Fact]
    public void RegistrySeparatesEngineCategoriesFromOctaneEditorCodesAndCapabilities()
    {
        Assert.Equal(172, WiredBoxRegistry.All.Count);
        var categories = WiredBoxRegistry.All.GroupBy(box => box.Category).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(25, categories[WiredBoxCategory.Trigger]);
        Assert.Equal(49, categories[WiredBoxCategory.Action]);
        Assert.Equal(42, categories[WiredBoxCategory.Condition]);
        Assert.Equal(20, categories[WiredBoxCategory.Selector]);
        Assert.Equal(28, categories[WiredBoxCategory.Addon]);
        Assert.Equal(8, categories[WiredBoxCategory.Variable]);
        Assert.All(WiredBoxRegistry.All, box => Assert.Equal(WiredBoxSupport.DescriptorOnly, box.Support));
        Assert.True(WiredBoxRegistry.TryGet("wf_act_send_signal", out var signal));
        Assert.Equal(33, signal.EditorCode);
        Assert.Equal(30, signal.TurboCode);
        Assert.True(WiredBoxRegistry.TryGet("wf_slc_furni_area", out var selector));
        Assert.Equal(28, selector.EditorCode);
        Assert.Equal(7, selector.TurboCode);
        Assert.Equal(WiredBoxCategory.Action, selector.Envelope);
        Assert.True(WiredBoxRegistry.TryGet("wf_var_room", out var variable));
        Assert.Equal(72, variable.EditorCode);
        Assert.Equal(WiredBoxCategory.Action, variable.Envelope);
    }

    [Fact]
    public void OctaneSaveReadsPerBoxIntAndTextFieldsWithoutConsumingTurboSourceArrays()
    {
        var packet = Incoming(5, 1, 4, 2, 7, WiredSources.Selected, "forward:9", 2, 9, 10, 6, 0);
        Assert.True(WiredLegacyProtocol.TryRead(packet, WiredBoxCategory.Action, out var configuration));
        Assert.Equal(new[] { 1, 4, 2, 7, WiredSources.Selected }, configuration.IntParams);
        Assert.Equal("forward:9", configuration.Text);
        Assert.Equal(new uint[] { 9, 10 }, configuration.SelectedItems);
        Assert.Equal(6, configuration.Delay);
        Assert.Empty(configuration.FurniSources);
        Assert.False(packet.HasDataRemaining());
        Assert.False(WiredLegacyProtocol.TryRead(Incoming(101), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Incoming(0, "x", 0, -1, 0), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Incoming(0, "x", 1, -1, 0, 0), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Incoming(0, "x", 0, 0, 0, 123), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Incoming(2, 1), WiredBoxCategory.Action, out _));
    }

    [Theory]
    [InlineData("wf_trg_recv_signal", 15, false, true)]
    [InlineData("wf_cnd_has_var", 40, false, false)]
    [InlineData("wf_slc_furni_area", 28, true, true)]
    [InlineData("wf_var_room", 72, true, true)]
    public void ConfiguredOpenUsesExactLegacyFieldOrder(string name, int code, bool action, bool blocked)
    {
        var box = new ConfiguredBox(name);
        box.ApplyConfiguration(new() { IntParams = [200, 7], Text = "schema-json", SelectedItems = [8], Delay = 3 });
        var packet = new RecordingPacket();
        var composer = new WiredConfiguredConfigComposer(box);
        composer.Compose(packet);
        var expected = new List<object> { false, 100, 1, 8u, 91, 7u, "schema-json", 2, 200, 7, 0, code };
        if (action) expected.Add(3);
        if (blocked) expected.Add(0);
        Assert.Equal(expected, packet.Writes);
        Assert.Equal(action ? ServerPacketHeader.WiredEffectConfigComposer
            : blocked ? ServerPacketHeader.WiredTriggeRconfigComposer : ServerPacketHeader.WiredConditionConfigComposer, composer.MessageId);
    }

    [Fact]
    public void EditorProjectionShowsCurrentValuesWithoutChangingSavedConfiguration()
    {
        var box = new ConfiguredBox("wf_var_room");
        box.ApplyConfiguration(new() { Text = "saved metadata" });
        var saved = box.Configuration;
        box.EditorConfiguration = saved with { Text = "current value" };
        var packet = new RecordingPacket();
        new WiredConfiguredConfigComposer(box).Compose(packet);
        Assert.Contains("current value", packet.Writes);
        Assert.Same(saved, box.Configuration);
        Assert.Equal("saved metadata", box.Configuration.Text);
        Assert.NotEqual(ServerPacketHeader.TradingCompleteComposer, ServerPacketHeader.WiredClickSettingsComposer);
        Assert.Equal(9477u, ServerPacketHeader.WiredClickSettingsComposer);
    }

    [Fact]
    public void FailedValidationOrPersistenceNeverPublishesNewLiveSettings()
    {
        var box = new ConfiguredBox("wf_act_send_signal");
        var original = box.Configuration;
        var store = new RecordingStore();
        box.Reject = true;
        Assert.False(WiredConfigurationSave.TrySave(box, new() { Text = "rejected" }, store, out _));
        Assert.Same(original, box.Configuration);
        Assert.Empty(store.Saved);
        box.Reject = false;
        store.Throw = true;
        Assert.Throws<InvalidOperationException>(() => WiredConfigurationSave.TrySave(box, new() { Text = "not durable" }, store, out _));
        Assert.Same(original, box.Configuration);
        store.Throw = false;
        Assert.False(WiredConfigurationSave.TrySave(box, new() { SelectedItems = [999] }, store, out _, id => id == 1));
        Assert.Empty(store.Saved);
        Assert.Same(original, box.Configuration);
        Assert.True(WiredConfigurationSave.TrySave(box, new() { Text = "durable" }, store, out _));
        Assert.Same(store.Saved.Single(), box.Configuration);
    }

    [Fact]
    public void DescriptorOnlyBoxesCannotPersistAnApparentlySuccessfulEdit()
    {
        var box = new ConfiguredBox("wf_act_send_signal", WiredBoxSupport.DescriptorOnly);
        var original = box.Configuration;
        var store = new RecordingStore();
        Assert.False(WiredConfigurationSave.TrySave(box, new() { Text = "unsupported" }, store, out var error));
        Assert.NotEmpty(error);
        Assert.Empty(store.Saved);
        Assert.Same(original, box.Configuration);
    }

    [Fact]
    public void StructuredPersistencePreservesNamedLegacySourcesAndVariableTokens()
    {
        var config = new WiredConfiguration
        {
            IntParams = [1, 0, 42, 11, 100], Text = "custom:8", SelectedItems = [8], SecondarySelectedItems = [9],
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("forward", WiredSources.Selected),
            UserSources = ImmutableDictionary<string, int>.Empty.Add("actor", WiredSources.ClickedUser),
            VariableIds = ["custom:8", "internal:@altitude"], Snapshots = [new(8, 5, 1, 2, 0.5, 4, "state")]
        };
        var restored = JsonSerializer.Deserialize<WiredConfiguration>(JsonSerializer.Serialize(config))!;
        Assert.True(WiredLegacyProtocol.IsWithinLimits(restored));
        Assert.Equal(config.IntParams.ToArray(), restored.IntParams.ToArray());
        Assert.Equal(config.VariableIds.ToArray(), restored.VariableIds.ToArray());
        Assert.Equal(100, restored.FurniSources["forward"]);
        Assert.Equal(11, restored.UserSources["actor"]);
        Assert.Equal(9u, restored.SecondarySelectedItems.Single());
        Assert.Equal(config.Snapshots.Single(), restored.Snapshots.Single());
        Assert.False(WiredLegacyProtocol.IsWithinLimits(config with { Version = 2 }));
        Assert.False(WiredLegacyProtocol.IsWithinLimits(config with { Text = new string('\u20ac', 32768) }));
    }

    [Fact]
    public void ModernRecognitionPreservesLegacyWiredIdFallback()
    {
        Assert.Equal(InteractionType.WiredSelector, ItemDataManager.ReadInteractionType("ordinary", "wf_slc_furni_area"));
        Assert.Equal(InteractionType.WiredVariable, ItemDataManager.ReadInteractionType("wf_var_room", "wired_effect"));
        Assert.Equal(InteractionType.WiredEffect, ItemDataManager.ReadInteractionType("custom_box", "wired_effect"));
        Assert.Equal(WiredBoxType.TriggerUserSays, ItemDataManager.ReadWiredType(1));
        Assert.True(new Item { Definition = new() { InteractionName = "wf_slc_furni_area" } }.IsWired);
    }

    [Fact]
    public void CanonicalRandomNameKeepsExplicitLegacyCategoryAndFactoryWhenWiredIdIsConstructible()
    {
        var wiredType = ItemDataManager.ReadWiredType(41);
        var definition = new ItemDefinition
        {
            ItemName = "wf_xtra_random", InteractionName = "wired_effect", WiredType = wiredType,
            InteractionType = ItemDataManager.ReadInteractionType("wf_xtra_random", "wired_effect", wiredType)
        };
        Assert.Equal(InteractionType.WiredEffect, definition.InteractionType);
        Assert.Equal(WiredBoxCategory.Addon, definition.WiredDescriptor!.Category);
        var item = new Item { Id = 7, Definition = definition };
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var handling = room.GetRoomItemHandler();
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
            .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handling)!;
        Assert.True(floor.TryAdd(item.Id, item));
        var legacy = new WiredComponent(room);
        var loaded = legacy.GenerateNewBox(item);
        Assert.NotNull(loaded);
        Assert.Equal(WiredBoxType.AddonRandomEffect, loaded.Type);
        Assert.True(legacy.IsEffect(item));
        Assert.True(legacy.AddBox(loaded));
        Assert.Contains(loaded, legacy.GetEffects(loaded));
        Assert.Equal(InteractionType.WiredAddon,
            ItemDataManager.ReadInteractionType("wf_xtra_random", "wired_effect", WiredBoxType.None));
        Assert.Equal(InteractionType.WiredSelector,
            ItemDataManager.ReadInteractionType("wf_slc_furni_area", "wired_effect", ItemDataManager.ReadWiredType(34)));
    }

    [Fact]
    public void RoomPublisherOwnsPersistenceAndPublicationWithoutHoldingTheBoxLock()
    {
        var box = new ConfiguredBox("wf_act_send_signal");
        var original = box.Configuration;
        var store = new RecordingStore();
        Assert.False(WiredConfigurationSave.TrySave(box, new() { Text = "detached" }, store, out _,
            publish: (live, validated, persist) =>
            {
                Assert.False(Monitor.IsEntered(live));
                return false;
            }));
        Assert.Empty(store.Saved);
        Assert.Same(original, box.Configuration);
        store.Throw = true;
        Assert.Throws<InvalidOperationException>(() => WiredConfigurationSave.TrySave(box, new() { Text = "failure" },
            store, out _, publish: (live, validated, persist) =>
            {
                persist();
                live.ApplyConfiguration(validated);
                return true;
            }));
        Assert.Same(original, box.Configuration);
        store.Throw = false;
        Assert.True(WiredConfigurationSave.TrySave(box, new() { Text = "durable" }, store, out _,
            publish: (live, validated, persist) =>
            {
                Assert.False(Monitor.IsEntered(live));
                Assert.Same(original, live.Configuration);
                persist();
                Assert.Same(store.Saved.Single(), validated);
                live.ApplyConfiguration(validated);
                return true;
            }));
        Assert.Equal("durable", box.Configuration.Text);
    }

    [Fact]
    public void DomainPersistenceReplacesDefaultStorageInsideRealEnginePublication()
    {
        var order = new List<string>();
        var fail = true;
        var box = new PersistingBox("wf_var_room", validated =>
        {
            order.Add("persist");
            if (fail) throw new IOException("Combined transaction rejected.");
            Assert.Equal("combined", validated.Text);
        });
        box.Applying = _ => order.Add("apply");
        var original = box.Configuration;
        var store = new RecordingStore();
        var engine = new WiredStackEngine(() => 0, _ => true, _ => true, _ => { }, _ => { });
        Assert.True(engine.Add(box));
        Assert.Throws<IOException>(() => WiredConfigurationSave.TrySave(box, new() { Text = "combined" }, store,
            out _, publish: engine.PublishConfigured));
        Assert.Same(original, box.Configuration);
        Assert.Equal(new[] { "persist" }, order);
        Assert.Empty(store.Saved);
        fail = false;
        order.Clear();
        Assert.True(WiredConfigurationSave.TrySave(box, new() { Text = "combined" }, store,
            out _, publish: engine.PublishConfigured));
        Assert.Equal(new[] { "persist", "apply" }, order);
        Assert.Equal("combined", box.Configuration.Text);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void SavePreparationCapturesSnapshotsBeforePureValidationAndDurablePublication()
    {
        var box = new ConfiguredBox("wf_act_match_to_sshot");
        var original = box.Configuration;
        var store = new RecordingStore();
        var prepared = false;
        Assert.True(WiredConfigurationSave.TrySave(box, new() { IntParams = [1, 0, 1], SelectedItems = [8] }, store,
            out _, existsInRoom: id => id == 8, prepare: (live, proposed) =>
            {
                Assert.Same(original, live.Configuration);
                Assert.Empty(store.Saved);
                prepared = true;
                return proposed with { Snapshots = [new(8, 5, 1, 2, 0.5, 4, "captured")] };
            }, publish: (live, validated, persist) =>
            {
                Assert.True(prepared);
                Assert.Equal("captured", box.ValidatedInput!.Snapshots.Single().State);
                Assert.Same(original, live.Configuration);
                persist();
                live.ApplyConfiguration(validated);
                return true;
            }));
        Assert.Equal("captured", store.Saved.Single().Snapshots.Single().State);
        Assert.Equal("captured", box.Configuration.Snapshots.Single().State);
        prepared = false;
        Assert.False(WiredConfigurationSave.TrySave(box, new() { SelectedItems = [999] }, store, out _,
            existsInRoom: id => id == 8, prepare: (_, proposed) => { prepared = true; return proposed; }));
        Assert.False(prepared);
    }

    [Fact]
    public void LegacyCycleEffectWritesSelectionBeforeEditorCode()
    {
        var item = new Item { Id = 7, Definition = new() { SpriteId = 91 } };
        var teleport = new TeleportUserBox(null!, item) { Delay = 3, StringData = string.Empty };
        var packet = new RecordingPacket();
        new WiredEffectConfigComposer(teleport, []).Compose(packet);
        Assert.Equal(new object[] { false, 15, 0, 91, 7u, "", 0, 0, 8, 3, 0 }, packet.Writes);
    }

    [Fact]
    public void LegacySnapshotConditionAndBotHandItemAdvertiseExactlyTheirWrittenParams()
    {
        var item = new Item { Id = 7, Definition = new() { SpriteId = 91 } };
        var condition = new FurniMatchStateAndPositionBox(null!, item) { StringData = "1;0;1" };
        var conditionPacket = new RecordingPacket();
        new WiredConditionConfigComposer(condition).Compose(conditionPacket);
        Assert.Equal(new object[] { false, 5, 0, 91, 7u, "1;0;1", 3, 1, 0, 1, 0, 0 }, conditionPacket.Writes);
        var bot = new BotGivesHandItemBox(null!, item) { StringData = "Bot;12" };
        var botPacket = new RecordingPacket();
        new WiredEffectConfigComposer(bot, []).Compose(botPacket);
        Assert.Equal(new object[] { false, 15, 0, 91, 7u, "Bot", 1, 12, 0, 24, 0, 0 }, botPacket.Writes);
        Assert.Equal(2, WiredBoxTypeUtility.GetWiredId(WiredBoxType.TriggerWalkOffFurni));
        Assert.Equal(2, WiredBoxTypeUtility.GetWiredId(WiredBoxType.ConditionTriggererOnFurni));
        Assert.Equal(7, WiredBoxTypeUtility.GetWiredId(WiredBoxType.ConditionFurniHasFurni));
        Assert.Equal(19, WiredBoxTypeUtility.GetWiredId(WiredBoxType.EffectKickUser));
        Assert.Equal(88, WiredBoxTypeUtility.GetWiredId(WiredBoxType.EffectSetRollerSpeed));
        Assert.Equal(119, WiredBoxTypeUtility.GetWiredId(WiredBoxType.EffectGiveUserBadge));
    }

    private static FlashIncomingPacket Incoming(params object[] values)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        foreach (var value in values)
        {
            if (value is int integer)
            {
                BinaryPrimitives.WriteInt32BigEndian(number, integer);
                stream.Write(number);
            }
            else
            {
                var text = Encoding.UTF8.GetBytes((string)value);
                BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)text.Length);
                stream.Write(number[..2]);
                stream.Write(text);
            }
        }
        return new() { Buffer = stream.ToArray() };
    }

    private sealed class RecordingStore : IWiredConfigurationStore
    {
        public List<WiredConfiguration> Saved { get; } = [];
        public bool Throw { get; set; }
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => Saved.LastOrDefault();
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration)
        {
            if (Throw) throw new InvalidOperationException("Database unavailable.");
            Saved.Add(configuration);
        }
    }

    private class ConfiguredBox : IWiredConfiguredItem, IWiredEditorConfigurationProvider
    {
        public ConfiguredBox(string name, WiredBoxSupport support = WiredBoxSupport.Implemented)
        {
            Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
            Descriptor = descriptor with { Support = support };
        }
        public WiredBoxDescriptor Descriptor { get; }
        public WiredConfiguration Configuration { get; private set; } = new();
        public WiredConfiguration? EditorConfiguration { get; set; }
        public WiredConfiguration GetEditorConfiguration() => EditorConfiguration ?? Configuration;
        public Action<WiredConfiguration>? Applying { get; set; }
        public bool Reject { get; set; }
        public WiredConfiguration? ValidatedInput { get; private set; }
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            ValidatedInput = proposed;
            validated = proposed;
            error = Reject ? "Rejected by box validation." : string.Empty;
            return !Reject;
        }
        public void ApplyConfiguration(WiredConfiguration validated)
        {
            Applying?.Invoke(validated);
            Configuration = validated;
        }
        public Item Item { get; set; } = new() { Id = 7, Definition = new() { SpriteId = 91 } };
        public Room Instance { get; set; } = null!;
        public WiredBoxType Type => WiredBoxType.None;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = string.Empty;
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = string.Empty;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments) => throw new NotSupportedException();
    }

    private sealed class PersistingBox(string name, Action<WiredConfiguration> persist)
        : ConfiguredBox(name), IWiredConfigurationPersistenceProvider
    {
        public void PersistConfiguration(WiredConfiguration validated) => persist(validated);
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value);
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
