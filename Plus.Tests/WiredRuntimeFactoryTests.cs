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
    [Theory]
    [InlineData("wf_xtra_varfx_hp", "wf_xtra_var_fx_health", InteractionType.WiredAddon)]
    [InlineData("wf_xtra_varfx_prog", "wf_xtra_var_fx_progress", InteractionType.WiredAddon)]
    [InlineData("wf_xtra_varfx_levelling", "wf_xtra_var_fx_level", InteractionType.WiredAddon)]
    [InlineData("wf_xtra_varfx_status", "wf_xtra_var_fx_status", InteractionType.WiredAddon)]
    [InlineData("wf_xtra_varfx_boss", "wf_xtra_var_fx_boss", InteractionType.WiredAddon)]
    [InlineData("wf_xtra_varfx_number", "wf_xtra_var_fx_number", InteractionType.WiredAddon)]
    [InlineData("wf_proto_trg_at_given_time", "wf_trg_at_given_time", InteractionType.WiredTrigger)]
    [InlineData("wf_proto_cnd_trggrer_on_frn", "wf_cnd_trggrer_on_frn", InteractionType.WiredCondition)]
    [InlineData("wf_ltdproto_act_toggle_state", "wf_act_toggle_state", InteractionType.WiredEffect)]
    public void ExplicitDatabaseInteractionLoadsTheOfficialFurniture(string classname, string canonicalName, InteractionType interaction)
    {
        // The database stores the asset classname and concrete server interaction separately.
        var definition = new ItemDefinition
        {
            ItemName = classname,
            InteractionName = canonicalName,
            InteractionType = ItemDataManager.ReadInteractionType(classname, canonicalName)
        };
        var item = new Item { Id = 1, Definition = definition };
        Assert.False(WiredBoxRegistry.TryGet(classname, out _));
        Assert.Equal(interaction, definition.InteractionType);
        Assert.True(item.IsWired);
        var facade = new WiredComponent(Room(), TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        var box = Assert.IsAssignableFrom<IWiredConfiguredItem>(facade.LoadWiredBox(item));
        Assert.Equal(canonicalName, box.Descriptor.CanonicalName);
        Assert.Equal(WiredBoxSupport.Implemented, box.Descriptor.Support);
        Assert.True(facade.TryGet(item.Id, out var registered));
        Assert.Same(box, registered);
    }

    private static Room Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1;
        room.OwnerId = 5;

        return room;
    }

    [Fact]
    public void RegistryProbeReportsOnlyConcreteFactorySupport()
    {
        var facade = new WiredComponent(Room(), TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        var boxes = WiredBoxRegistry.All.OrderBy(x => x.CanonicalName).Select((descriptor, index) =>
        {
            var item = new Item { Id = (uint)index + 1, Definition = new() { InteractionName = descriptor.CanonicalName } };
            var box = facade.CreateConfiguredBox(item);

            if (box != null) {
                Assert.Same(item, box.Item);
                Assert.Equal(descriptor.CanonicalName, box.Descriptor.CanonicalName);
                Assert.Equal(WiredBoxSupport.Implemented, box.Descriptor.Support);
            }

            return new { name = descriptor.CanonicalName, support = box?.Descriptor.Support.ToString() ?? "DescriptorOnly" };
        }).ToArray();
        Assert.Equal(182, boxes.Length);
        Assert.Contains(boxes, x => x.name == "wf_act_teleport_to" && x.support == "Implemented");
        Assert.All(boxes, x => Assert.Equal("Implemented", x.support));
        var auxiliaries = new[] { "wf_upcounter1", "wf_upcounter2", "wf_game_upcounter1", "wf_game_upcounter2", "wf_antenna1", "wf_antenna2" }
            .Select(name =>
            {
                var interaction = name.StartsWith("wf_antenna") ? "antenna" : name;
                var item = new Item { Definition = new() { ItemName = name, InteractionName = interaction, Type = ItemType.Floor } };

                return new { name, interaction, supported = WiredCounterController.Recognizes(item) || WiredStackEngine.IsSignalAntenna(item) };
            }).ToArray();
        Assert.All(auxiliaries, auxiliary => Assert.True(auxiliary.supported));

        if (Environment.GetEnvironmentVariable("WIRED_SUPPORT_LEDGER") is { Length: > 0 } output) {
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                engineCommit = Environment.GetEnvironmentVariable("WIRED_ENGINE_COMMIT"),
                registryCommit = "58a2ee4767379dc72e037cab4c061ff33066c0f5",
                boxes,
                auxiliaries,
                proof = new[] { "RegistryProbeReportsOnlyConcreteFactorySupport", "NativeCounterInteractorRetainsStateAndStartsOnlyWithRights", "NativeAntennaDeliversConfiguredSignalAndRejectsDetachedReceiver" }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    [Theory]
    [InlineData("wf_trg_says_something")]
    [InlineData("wf_act_teleport_to")]
    public void DetachedPromotionCandidateAcceptsDescriptorOverride(string name)
    {
        var facade = new WiredComponent(Room(), TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
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
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
        var item = new Item { Id = 10, Definition = new() { ItemName = "legacy_custom_name", WiredType = type } };
        floor[item.Id] = item;
        var config = new WiredConfiguration { Text = text, IntParams = Enumerable.Repeat(0, count).ToImmutableArray() };
        var store = new SidecarStore(name, config);
        var facade = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, store, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
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
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        var item = new Item { Id = 11, Definition = new() { ItemName = "legacy_custom_name", WiredType = WiredBoxType.TriggerUserSays } };
        var store = new FailingLoadStore();
        var facade = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, store, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        Assert.Null(facade.LoadWiredBox(item));
        Assert.Equal(1, store.Reads);
        Assert.False(facade.TryGet(item.Id, out _));
    }

    private sealed class FailingLoadStore : IWiredConfigurationStore
    {
        public int Reads { get; private set; }
        public WiredConfiguration? Load(uint id, WiredBoxDescriptor descriptor)
        {
            Reads++;
            throw new InvalidDataException("Saved bytes are unreadable.");
        }
        public void Reset(IReadOnlyCollection<uint> itemIds) => throw new NotSupportedException();
        public void Save(uint id, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => throw new NotSupportedException();
    }

    private sealed class SidecarStore(string name, WiredConfiguration config) : IWiredConfigurationStore
    {
        public ModernWiredRuntimeTests.StoredRuntimeRow Row { get; } = new(10, name, 1, JsonSerializer.Serialize(config));
        public bool WasRead;
        public WiredConfiguration? Load(uint id, WiredBoxDescriptor descriptor)
        {
            Assert.Equal(10u, id);
            Assert.Equal(name, descriptor.CanonicalName);
            WasRead = true;

            return new WiredConfigurationStore(new ModernWiredRuntimeTests.StoredRuntimeRowsDatabase([Row])).Load(id, descriptor);
        }
        public void Save(uint id, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => throw new NotSupportedException();
        public void Reset(IReadOnlyCollection<uint> itemIds) => throw new NotSupportedException();
    }


    [Theory]
    [InlineData("wrong_name")]
    [InlineData("wrong_schema")]
    [InlineData("mismatched_json_version")]
    public void RealSidecarLoaderRefusesIncompatibleCapturedRows(string failure)
    {
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_says_something", out var descriptor));
        var original = new WiredConfiguration { Text = "hello", IntParams = [0, 0, 0] };
        var row = new ModernWiredRuntimeTests.StoredRuntimeRow(10, descriptor.CanonicalName, 1, JsonSerializer.Serialize(original));
        row = failure switch
        {
            "wrong_name" => row with { Name = "wf_act_teleport_to" },
            "wrong_schema" => row with { Version = 3 },
            "mismatched_json_version" => row with { Json = JsonSerializer.Serialize(original with { Version = 2 }) },
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var store = new WiredConfigurationStore(new ModernWiredRuntimeTests.StoredRuntimeRowsDatabase([row]));
        Assert.Throws<InvalidDataException>(() => store.Load(10, descriptor));
    }



}
