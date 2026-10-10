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
        Assert.Equal(182, WiredBoxRegistry.All.Count);
        var categories = WiredBoxRegistry.All.GroupBy(box => box.Category).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(27, categories[WiredBoxCategory.Trigger]);
        Assert.Equal(53, categories[WiredBoxCategory.Action]);
        Assert.Equal(44, categories[WiredBoxCategory.Condition]);
        Assert.Equal(20, categories[WiredBoxCategory.Selector]);
        Assert.Equal(30, categories[WiredBoxCategory.Addon]);
        Assert.Equal(8, categories[WiredBoxCategory.Variable]);
        Assert.All(WiredBoxRegistry.All, box => Assert.Equal(WiredBoxSupport.DescriptorOnly, box.Support));
        Assert.True(WiredBoxRegistry.TryGet("wf_act_send_signal", out var signal));
        Assert.Equal(30, signal.EditorCode);
        Assert.True(WiredBoxRegistry.TryGet("wf_slc_furni_area", out var selector));
        Assert.Equal(7, selector.EditorCode);
        Assert.Equal(WiredBoxCategory.Selector, selector.Envelope);
        Assert.True(WiredBoxRegistry.TryGet("wf_var_room", out var variable));
        Assert.Equal(2, variable.EditorCode);
        Assert.Equal(WiredBoxCategory.Variable, variable.Envelope);
    }

    [Fact]
    public void NativeSaveReadsOwnedFieldsAndSourceArraysAsOneCanonicalBody()
    {
        // owned ints, text, primary items, delay, furni sources, user sources, variables, secondary items.
        var packet = Incoming(5, 1, 4, 2, 7, WiredSources.Selected, "forward:9", 2, 9, 10, 6, 1, 100, 1, 11, 0, 1, -12);
        Assert.True(WiredLegacyProtocol.TryReadNative(packet, WiredBoxCategory.Action, out var native));
        Assert.Equal(new[] { 1, 4, 2, 7, WiredSources.Selected }, native.OwnedIntParams);
        Assert.Equal("forward:9", native.Text);
        Assert.Equal(new uint[] { 9, 10 }, native.PrimaryItems.Select(item => item.ItemId));
        Assert.Equal(6, native.Delay);
        Assert.Equal(new[] { 100 }, native.FurniSourceTypes);
        Assert.Equal(new[] { 11 }, native.UserSourceTypes);
        Assert.Equal(12u, native.SecondaryItems.Single().ItemId);
        Assert.True(native.SecondaryItems.Single().Wall);
        Assert.False(packet.HasDataRemaining());
        Assert.False(WiredLegacyProtocol.TryReadNative(Incoming(WiredConfigurationLimits.IntParams + 1), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryReadNative(Incoming(0, "x", -1), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryReadNative(Incoming(0, "x", 1, 0, 0, 0, 0, 0, 0), WiredBoxCategory.Action, out _));
        Assert.True(WiredLegacyProtocol.TryReadNative(Incoming(0, "x", 1, -1, 0, 0, 0, 0, 0), WiredBoxCategory.Action, out var wall));
        Assert.True(wall.PrimaryItems.Single().Wall);
        Assert.False(WiredLegacyProtocol.TryReadNative(Incoming(0, "x", 0, 0, 0, 0, 0, 0, 123), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryReadNative(Incoming(2, 1), WiredBoxCategory.Action, out _));
    }

    [Fact]
    public void NeighborhoodSaveAcceptsAllEightyOneEditorTilesThroughThePacketEnvelope()
    {
        var fields = new List<int> { 0, 0, 0, 0, 0, 81 };

        for (var y = -4; y <= 4; y++) {
            for (var x = -4; x <= 4; x++) {
                fields.Add(x);
                fields.Add(y);
            }
        }

        var payload = new List<object> { fields.Count };
        payload.AddRange(fields.Cast<object>());
        payload.AddRange(new object[] { "", 0, 0, 0, 0, 0, 0 });
        Assert.True(WiredLegacyProtocol.TryReadNative(Incoming(payload.ToArray()), WiredBoxCategory.Action, out var native));
        Assert.Equal(fields, native.OwnedIntParams);
    }

    [Fact]
    public void NativeConfigurationComposersKeepTheirOwnHeaders()
    {
        Assert.NotEqual(ServerPacketHeader.TradingCompleteComposer, ServerPacketHeader.WiredClickSettingsComposer);
        Assert.Equal(917u, ServerPacketHeader.WiredClickSettingsComposer);
    }

    [Fact]
    public void FailedValidationOrPersistenceNeverPublishesNewLiveSettings()
    {
        var box = new ConfiguredBox(Mapped);
        var original = box.Configuration;
        var store = new RecordingStore();
        box.Reject = true;
        Assert.False(WiredConfigurationSave.TrySave(box, Proposed(box, 1), store, out _));
        Assert.Same(original, box.Configuration);
        Assert.Empty(store.Saved);
        box.Reject = false;
        store.Throw = true;
        Assert.Throws<InvalidOperationException>(() => WiredConfigurationSave.TrySave(box, Proposed(box, 2), store, out _));
        Assert.Same(original, box.Configuration);
        store.Throw = false;
        Assert.False(WiredConfigurationSave.TrySave(box, Proposed(box, 3, 999), store, out _, id => id == 1));
        Assert.Empty(store.Saved);
        Assert.Same(original, box.Configuration);
        Assert.True(WiredConfigurationSave.TrySave(box, Proposed(box, 4), store, out _));
        Assert.Same(store.Saved.Single(), box.Configuration);
    }

    [Fact]
    public void DescriptorOnlyBoxesCannotPersistAnApparentlySuccessfulEdit()
    {
        var box = new ConfiguredBox(Mapped, WiredBoxSupport.DescriptorOnly);
        var original = box.Configuration;
        var store = new RecordingStore();
        Assert.False(WiredConfigurationSave.TrySave(box, Proposed(box, 1), store, out var error));
        Assert.NotEmpty(error);
        Assert.Empty(store.Saved);
        Assert.Same(original, box.Configuration);
    }

    [Fact]
    public void StructuredPersistencePreservesNamedLegacySourcesAndVariableTokens()
    {
        var config = new WiredConfiguration
        {
            IntParams = [1, 0, 42, 11, 100],
            Text = "custom:8",
            SelectedItems = [8],
            SecondarySelectedItems = [9],
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("forward", WiredSources.Selected),
            UserSources = ImmutableDictionary<string, int>.Empty.Add("actor", WiredSources.ClickedUser),
            VariableIds = ["custom:8", "internal:@altitude"],
            Snapshots = [new(8, 5, 1, 2, 0.5, 4, "state")]
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
    public void RoomPublisherOwnsPersistenceAndPublicationWithoutHoldingTheBoxLock()
    {
        var box = new ConfiguredBox(Mapped);
        var original = box.Configuration;
        var store = new RecordingStore();
        Assert.False(WiredConfigurationSave.TrySave(box, Proposed(box, 1), store, out _,
            publish: (live, validated, persist) =>
            {
                Assert.False(Monitor.IsEntered(live));

                return false;
            }));
        Assert.Empty(store.Saved);
        Assert.Same(original, box.Configuration);
        store.Throw = true;
        Assert.Throws<InvalidOperationException>(() => WiredConfigurationSave.TrySave(box, Proposed(box, 2),
            store, out _, publish: (live, validated, persist) =>
            {
                persist();
                live.ApplyConfiguration(validated);

                return true;
            }));
        Assert.Same(original, box.Configuration);
        store.Throw = false;
        Assert.True(WiredConfigurationSave.TrySave(box, Proposed(box, 4), store, out _,
            publish: (live, validated, persist) =>
            {
                Assert.False(Monitor.IsEntered(live));
                Assert.Same(original, live.Configuration);
                persist();
                Assert.Same(store.Saved.Single(), validated);
                live.ApplyConfiguration(validated);

                return true;
            }));
        Assert.Equal(4, box.Configuration.Delay);
    }

    [Fact]
    public void DomainPersistenceReplacesDefaultStorageInsideRealEnginePublication()
    {
        var order = new List<string>();
        var fail = true;
        var box = new PersistingBox(Mapped, validated =>
        {
            order.Add("persist");

            if (fail) {
                throw new IOException("Combined transaction rejected.");
            }

            Assert.Equal(5, validated.Delay);
        });
        box.Applying = _ => order.Add("apply");
        var original = box.Configuration;
        var store = new RecordingStore();
        var engine = new WiredStackEngine(() => 0, _ => true, _ => true, _ => { }, _ => { });
        Assert.True(engine.Add(box));
        Assert.Throws<IOException>(() => WiredConfigurationSave.TrySave(box, Proposed(box, 5), store,
            out _, publish: engine.PublishConfigured));
        Assert.Same(original, box.Configuration);
        Assert.Equal(new[] { "persist" }, order);
        Assert.Empty(store.Saved);
        fail = false;
        order.Clear();
        Assert.True(WiredConfigurationSave.TrySave(box, Proposed(box, 5), store,
            out _, publish: engine.PublishConfigured));
        Assert.Equal(new[] { "persist", "apply" }, order);
        Assert.Equal(5, box.Configuration.Delay);
        Assert.Empty(store.Saved);
    }




    // A bound native-compiled teleport draft stands in for any mapped card; the delay distinguishes saves.
    private const string Mapped = "wf_act_teleport_to";

    private static WiredConfiguration Proposed(ConfiguredBox box, int delay, params uint[] picks)
    {
        var native = WiredNativeEditorProjection.DefaultNative(box.Descriptor) with
        {
            Delay = delay,
            PrimaryItems = [.. picks.Select(id => new WiredNativeItemReference(id, false))]
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, native, out var runtime));

        return runtime;
    }

    private static FlashIncomingPacket Incoming(params object[] values)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];

        foreach (var value in values) {
            if (value is int integer) {
                BinaryPrimitives.WriteInt32BigEndian(number, integer);
                stream.Write(number);
            }
            else {
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
            if (Throw) {
                throw new InvalidOperationException("Database unavailable.");
            }

            Saved.Add(configuration);
        }
        public void Reset(IReadOnlyCollection<uint> itemIds) => Saved.Clear();
    }

    private class ConfiguredBox : IWiredConfiguredItem
    {
        public ConfiguredBox(string name, WiredBoxSupport support = WiredBoxSupport.Implemented)
        {
            Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
            Descriptor = descriptor with { Support = support };
        }
        public WiredBoxDescriptor Descriptor { get; }
        public WiredConfiguration Configuration { get; private set; } = new();
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
