using Plus.Database;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables.Fx;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>One room's production variable state. The room facade owns and calls these lifecycle methods.</summary>
public sealed partial class WiredRoomVariables
{
    private readonly Room _room;
    private readonly TimeProvider _clock;
    private readonly WiredVariableConfigurationPersistence _persistence;
    private readonly Dictionary<uint, WiredVariableDefinitionBox> _definitions = [];
    private readonly Plus.HabboHotel.Quests.IQuestManager? _quests;
    public WiredVariableModule Module { get; }
    public WiredVariableEditor Editor { get; }
    public WiredVariableFxTracker Fx { get; }
    public IReadOnlyCollection<WiredVariableDefinitionBox> Definitions => _definitions.Values.ToArray();
    public WiredVariableCatalog Catalog()
    {
        var definitions = Module.DescribeDefinitions(_definitions.Keys).Select(description => description with
        { TextConnector = MetadataOn(description.Definition.ItemId, "wf_xtra_var_text_connector")?.TextConnector ?? new Dictionary<int, string>() }).ToArray();

        return new(definitions.Concat(definitions.SelectMany(DerivedDescriptions)).ToArray());
    }
    public bool FxDirty { get; private set; } = true;

    public WiredRoomVariables(Room room, IDatabase database, TimeProvider clock,
        Func<WiredVariableReference, WiredVariableHolder, WiredVariableFrame, long?>? builtinRead = null,
        Func<WiredVariableReference, WiredVariableHolder, int, WiredVariableFrame, bool>? builtinWrite = null,
        Action<Item, WiredVariableFrame>? stateChanged = null, IItemTravelStore? travelStore = null,
        Plus.HabboHotel.Quests.IQuestManager? quests = null)
    {
        _room = room;
        _clock = clock;
        _quests = quests;
        Module = new(room.Id, new DatabaseWiredVariableDirectory(database), new DatabaseWiredVariableStore(database), clock,
            new RoomWiredBuiltinVariables(room, builtinRead, builtinWrite, stateChanged, new RoomItemMetadataStore(database), travelStore, quests, QuestBinding), ResolveDerived);
        Editor = new(Module);
        Fx = new(Module);
        _persistence = new(database, Module, clock);
    }

    public IWiredConfiguredItem? CreateBox(Item item)
    {
        if (item.Definition.WiredDescriptor is { } descriptor) {
            if (descriptor.CanonicalName == "wf_trg_var_changed") {
                return new WiredVariableChangedTrigger(_room, item, descriptor);
            }

            if (descriptor.CanonicalName == "wf_xtra_text_input_variable") {
                return new WiredVariableTextInputBox(_room, item, descriptor);
            }

            if (WiredVariableMetadataBox.Supports(descriptor.CanonicalName)) {
                return new WiredVariableMetadataBox(_room, item, descriptor);
            }

            if (WiredVariableAddonBox.Supports(descriptor.CanonicalName)) {
                return new WiredVariableAddonBox(_room, item, descriptor, Module,
                id => MetadataOn(id, "wf_xtra_var_text_connector")?.TextConnector ?? new Dictionary<int, string>());
            }
        }

        return WiredVariableBoxFactory.Create(_room, item, Module, _clock, _persistence);
    }
    /// <summary>Call after hydration from the companion configuration store. Database failures must abort activation.</summary>
    public void ConfigurationLoaded(IWiredConfiguredItem box)
    {
        if (box is WiredVariableTextInputBox textInput) {
            lock (_membershipGate) {
                _textInputs[box.Item.Id] = textInput;
                _membershipEpoch++;
            }

            return;
        }

        if (box is WiredVariableMetadataBox metadata) {
            lock (_membershipGate) {
                _metadata[box.Item.Id] = metadata;
                _membershipEpoch++;
            }

            FxDirty = true;

            return;
        }

        if (box is not WiredVariableDefinitionBox definition || !definition.HasPersistedConfiguration) {
            return;
        }

        lock (_membershipGate) {
            _definitions[box.Item.Id] = definition;
            _membershipEpoch++;
        }

        if (definition.Descriptor.CanonicalName == "wf_var_room" && !Module.InitializeGlobal(box.Item.Id)) {
            throw new InvalidOperationException("The room variable could not be initialized from its authoritative definition.");
        }

        FxDirty = true;
    }
    /// <summary>Memory-only publication hook, called after atomic persistence and ApplyConfiguration.</summary>
    public void ConfigurationSaved(IWiredConfiguredItem box)
    {
        lock (_membershipGate) {
            if (box is WiredVariableTextInputBox textInput) {
                _textInputs[box.Item.Id] = textInput;
            }

            if (box is WiredVariableMetadataBox metadata) {
                _metadata[box.Item.Id] = metadata;
            }

            if (box is WiredVariableDefinitionBox definition) {
                _definitions[box.Item.Id] = definition;
            }

            _membershipEpoch++;
        }

        FxDirty = true;
    }
    public void HolderLeft(WiredVariableHolder holder)
    {
        Module.HolderLeft(holder);
        Fx.DetachHolder(holder);

        if (holder.Target == WiredVariableTarget.User && holder.CanPersist) {
            Fx.RemoveViewer(holder.StableId);
        }

        FxDirty = true;
    }
    public void ItemDetached(Item item) => ItemDetached(item.Id, WiredVariableRuntimeFrames.FurniHolder(item));
    public void ItemDetached(uint itemId)
    {
        var item = _room.GetRoomItemHandler()?.GetItem(itemId);

        if (item is not null) {
            ItemDetached(item);

            return;
        }

        // Without an attached Item, only durable identity is known. Temporary callers must pass the actual Item.
        ItemDetached(itemId, new(WiredVariableTarget.Furni, itemId, unchecked((int)itemId)));
    }
    private void ItemDetached(uint itemId, WiredVariableHolder? holder)
    {
        lock (_membershipGate) {
            _textInputs.Remove(itemId);
            _metadata.Remove(itemId);
            _definitions.Remove(itemId);
            _membershipEpoch++;
        }

        Module.DetachDefinition(itemId);

        if (holder is { } detached) {
            HolderLeft(detached);
        }

        FxDirty = true;
    }
    /// <summary>Only actual deletion of an owning item, never inventory pickup.</summary>
    public int DefinitionDeleted(uint itemId)
    {
        var removed = Module.DeleteDefinition(itemId);
        ItemDetached(itemId);

        return removed;
    }
    public IReadOnlyList<WiredVariableChange> DrainChanges()
    {
        var changes = Module.DrainChanges();

        if (changes.Count > 0) {
            FxDirty = true;
        }

        return changes;
    }
    public void InvalidateFx() => FxDirty = true;
    public void FxFlushed() => FxDirty = false;
}
