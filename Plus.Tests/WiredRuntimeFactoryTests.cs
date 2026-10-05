using System.Runtime.CompilerServices;
using System.Reflection;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Text.Json;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public sealed class WiredRuntimeFactoryTests
{
    private static Room Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1; room.OwnerId = 5;
        return room;
    }

    [Fact]
    public void RegistryProbeReportsOnlyConcreteFactorySupport()
    {
        var facade = new WiredComponent(Room(), TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);
        var boxes = WiredBoxRegistry.All.OrderBy(x => x.CanonicalName).Select((descriptor, index) =>
        {
            var item = new Item { Id = (uint)index + 1, Definition = new() { InteractionName = descriptor.CanonicalName } };
            var box = facade.CreateConfiguredBox(item);
            if (box != null)
            {
                Assert.Same(item, box.Item);
                Assert.Equal(descriptor.CanonicalName, box.Descriptor.CanonicalName);
                Assert.Equal(WiredBoxSupport.Implemented, box.Descriptor.Support);
            }
            return new { name = descriptor.CanonicalName, support = box?.Descriptor.Support.ToString() ?? "DescriptorOnly" };
        }).ToArray();
        Assert.Equal(172, boxes.Length);
        Assert.Contains(boxes, x => x.name == "wf_act_teleport_to" && x.support == "Implemented");
        Assert.All(boxes, x => Assert.Equal("Implemented", x.support));
        var auxiliaries = new[] { "wf_upcounter1", "wf_upcounter2", "wf_game_upcounter1", "wf_game_upcounter2", "wf_antenna1", "wf_antenna2" }
            .Select(name =>
            {
                var interaction = name.StartsWith("wf_antenna") ? "antenna" : name;
                var item = new Item { Definition = new() { ItemName = name, InteractionName = interaction, Type = ItemType.Floor } };
                return new { name, interaction, supported = WiredCounterController.Recognizes(item) || WiredStackEngine.IsSignalReceiver(item) };
            }).ToArray();
        Assert.All(auxiliaries, auxiliary => Assert.True(auxiliary.supported));
        if (Environment.GetEnvironmentVariable("WIRED_SUPPORT_LEDGER") is { Length: > 0 } output)
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                engineCommit = Environment.GetEnvironmentVariable("WIRED_ENGINE_COMMIT"),
                registryCommit = "58a2ee4767379dc72e037cab4c061ff33066c0f5", boxes,
                auxiliaries,
                proof = new[] { "RegistryProbeReportsOnlyConcreteFactorySupport", "NativeCounterInteractorRetainsStateAndStartsOnlyWithRights", "NativeAntennaDeliversConfiguredSignalAndRejectsDetachedReceiver" }
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Theory]
    [InlineData("wf_trg_says_something")]
    [InlineData("wf_act_teleport_to")]
    public void DetachedPromotionCandidateAcceptsDescriptorOverride(string name)
    {
        var facade = new WiredComponent(Room(), TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var definition = new ItemDefinition { ItemName = "legacy_custom_name" };
        var candidate = facade.CreateConfiguredBox(new() { Id = 1, Definition = definition }, descriptor);
        Assert.NotNull(candidate);
        Assert.Equal(name, candidate.Descriptor.CanonicalName);
        Assert.Null(definition.WiredDescriptor);
        Assert.False(facade.TryGet(1, out _));
    }

    [Theory]
    [InlineData(WiredBoxType.TriggerUserSays, "wf_trg_says_something", "hello", 3)]
    [InlineData(WiredBoxType.EffectTeleportToFurni, "wf_act_teleport_to", "", 3)]
    public void CustomNameLoadsCanonicalSidecarThroughLegacyDescriptor(WiredBoxType type, string name, string text, int count)
    {
        var room = Room();
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
        var item = new Item { Id = 10, Definition = new() { ItemName = "legacy_custom_name", WiredType = type } };
        floor[item.Id] = item;
        var config = new WiredConfiguration { Text = text, IntParams = Enumerable.Repeat(0, count).ToImmutableArray() };
        var store = new SidecarStore(name, config);
        var facade = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, store, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);
        var loaded = Assert.IsAssignableFrom<IWiredConfiguredItem>(facade.LoadWiredBox(item));
        Assert.Equal(name, loaded.Descriptor.CanonicalName);
        Assert.Equal(text, loaded.Configuration.Text);
        Assert.True(store.WasRead);
        Assert.Null(item.Definition.WiredDescriptor);
        Assert.True(facade.TryGet(item.Id, out var registered));
        Assert.Same(loaded, registered);
    }

    [Fact]
    public void InjectedStoreFailureLeavesSavedBytesUnregisteredAndUnpublished()
    {
        var room = Room();
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        var item = new Item { Id = 11, Definition = new() { ItemName = "legacy_custom_name", WiredType = WiredBoxType.TriggerUserSays } };
        var store = new FailingLoadStore();
        var facade = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, store, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);
        Assert.Null(facade.LoadWiredBox(item));
        Assert.Equal(1, store.Reads);
        Assert.False(facade.TryGet(item.Id, out _));
    }

    private sealed class FailingLoadStore : IWiredConfigurationStore
    {
        public int Reads { get; private set; }
        public WiredConfiguration? Load(uint id, WiredBoxDescriptor descriptor)
        { Reads++; throw new InvalidDataException("Saved bytes are unreadable."); }
        public void Save(uint id, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => throw new NotSupportedException();
    }

    private sealed class SidecarStore(string name, WiredConfiguration config) : IWiredConfigurationStore
    {
        public bool WasRead;
        public WiredConfiguration? Load(uint id, WiredBoxDescriptor descriptor)
        { Assert.Equal(10u, id); Assert.Equal(name, descriptor.CanonicalName); WasRead = true; return config; }
        public void Save(uint id, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => throw new NotSupportedException();
        public void Reset(IReadOnlyCollection<uint> itemIds) => throw new NotSupportedException();
    }

    [Fact]
    public void CustomCommandCannotBePromotedToGenericSpeech()
    {
        var facade = new WiredComponent(Room(), TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_says_something", out var descriptor));
        var item = new Item { Id = 1, Definition = new() { WiredType = WiredBoxType.TriggerUserSaysCommand } };
        var legacy = facade.GenerateNewBox(item);
        Assert.NotNull(legacy);
        Assert.Null(facade.CreateConfiguredBox(item, descriptor));
        Assert.Same(legacy, WiredBoxLoading.Select(legacy, null, null));
    }

    [Theory]
    [InlineData("wf_var_room", "score", 10, 7)]
    [InlineData("wf_var_user", "score", 1, 10)]
    [InlineData("wf_var_reference", "{\"variableName\":\"alias\",\"sourceTargetType\":0,\"sourceRoomId\":2,\"sourceVariableItemId\":20,\"readOnly\":true}", -1, -1)]
    public void UnsavedLoadedDefinitionFirstPublishesOnlyAfterDurability(string name, string text, int first, int second)
    {
        var room = Room();
        var facade = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance);
        var item = new Item { Id = 10, Definition = new() { InteractionName = name } };
        var box = Assert.IsType<WiredVariableDefinitionBox>(WiredBoxLoading.Select(null, facade.CreateConfiguredBox(item), null));
        Assert.False(box.HasPersistedConfiguration);
        facade.Variables.ConfigurationLoaded(box);
        Assert.Empty(facade.Variables.Definitions);
        var saved = new WiredConfiguration { Text = text, IntParams = first < 0 ? [] : [first, second] };
        Assert.True(box.TryValidateConfiguration(saved, out var validated, out var error), error);
        var engine = new WiredStackEngine(() => 0, current => ReferenceEquals(box, current), _ => true, _ => { }, error => throw error);
        engine.Add(box);
        WiredConfiguration? durable = null;
        Assert.Throws<IOException>(() => engine.PublishConfigured(box, validated, () => throw new IOException("storage unavailable")));
        Assert.False(box.HasPersistedConfiguration);
        Assert.True(engine.PublishConfigured(box, validated, () =>
        {
            Assert.False(box.HasPersistedConfiguration); // A real provider uses this to expect a missing sidecar on first save.
            durable = validated;
        }));
        Assert.True(box.HasPersistedConfiguration);
        Assert.Equal(validated, box.Configuration);
        var reloaded = Assert.IsType<WiredVariableDefinitionBox>(WiredBoxLoading.Select(null, facade.CreateConfiguredBox(item), durable));
        Assert.True(reloaded.HasPersistedConfiguration);
        Assert.Equal(validated, reloaded.Configuration);
    }
}
