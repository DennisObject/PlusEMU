using Plus.Database;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables.Fx;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>One room's production variable state. The room facade owns and calls these lifecycle methods.</summary>
public sealed partial class WiredRoomVariables
{
    private readonly Room _room;
    private readonly Func<long> _nowMs;
    private readonly WiredVariableConfigurationPersistence _persistence;
    private readonly Dictionary<uint, WiredVariableDefinitionBox> _definitions = [];
    public WiredVariableModule Module { get; }
    public WiredVariableEditor Editor { get; }
    public WiredVariableFxTracker Fx { get; }
    public IReadOnlyCollection<WiredVariableDefinitionBox> Definitions => _definitions.Values.ToArray();
    public WiredVariableCatalog Catalog() => new(Module.DescribeDefinitions(_definitions.Keys).Select(description =>
        description with { TextConnector = MetadataOn(description.Definition.ItemId, "wf_xtra_var_text_connector")?.TextConnector
            ?? new Dictionary<int, string>() }).ToArray());
    public bool FxDirty { get; private set; } = true;

    public WiredRoomVariables(Room room, IDatabase database, Func<long> nowMs,
        Func<WiredVariableReference, WiredVariableHolder, WiredVariableFrame, int?>? builtinRead = null,
        Func<WiredVariableReference, WiredVariableHolder, int, WiredVariableFrame, bool>? builtinWrite = null)
    {
        _room = room; _nowMs = nowMs;
        Module = new(room.Id, new DatabaseWiredVariableDirectory(database), new DatabaseWiredVariableStore(database), nowMs,
            new RoomWiredBuiltinVariables(room, builtinRead, builtinWrite));
        Editor = new(Module); Fx = new(Module);
        _persistence = new(database, Module, nowMs);
    }

    public IWiredConfiguredItem? CreateBox(Item item)
    {
        if (item.Definition.WiredDescriptor is { } descriptor)
        {
            if (WiredVariableMetadataBox.Supports(descriptor.CanonicalName)) return new WiredVariableMetadataBox(_room, item, descriptor);
            if (WiredVariableAddonBox.Supports(descriptor.CanonicalName)) return new WiredVariableAddonBox(_room, item, descriptor, Module,
                id => MetadataOn(id, "wf_xtra_var_text_connector")?.TextConnector ?? new Dictionary<int, string>());
        }
        return WiredVariableBoxFactory.Create(_room, item, Module, _nowMs, _persistence);
    }
    /// <summary>Call after hydration from the companion configuration store. Database failures must abort activation.</summary>
    public void ConfigurationLoaded(IWiredConfiguredItem box)
    {
        if (box is WiredVariableMetadataBox metadata) { _metadata[box.Item.Id] = metadata; FxDirty = true; return; }
        if (box is not WiredVariableDefinitionBox definition || !definition.HasPersistedConfiguration) return;
        _definitions[box.Item.Id] = definition;
        if (definition.Descriptor.CanonicalName == "wf_var_room" && !Module.InitializeGlobal(box.Item.Id))
            throw new InvalidOperationException("The room variable could not be initialized from its authoritative definition.");
        FxDirty = true;
    }
    /// <summary>Memory-only publication hook, called after atomic persistence and ApplyConfiguration.</summary>
    public void ConfigurationSaved(IWiredConfiguredItem box)
    {
        if (box is WiredVariableMetadataBox metadata) _metadata[box.Item.Id] = metadata;
        if (box is WiredVariableDefinitionBox definition)
        {
            _definitions[box.Item.Id] = definition;
        }
        FxDirty = true;
    }
    public void HolderLeft(WiredVariableHolder holder)
    {
        Module.HolderLeft(holder); Fx.DetachHolder(holder);
        if (holder.Target == WiredVariableTarget.User && holder.CanPersist) Fx.RemoveViewer(holder.StableId);
        FxDirty = true;
    }
    public void ItemDetached(Item item) => ItemDetached(item.Id, WiredVariableRuntimeFrames.FurniHolder(item));
    public void ItemDetached(uint itemId)
    {
        var item = _room.GetRoomItemHandler()?.GetItem(itemId);
        if (item is not null) { ItemDetached(item); return; }
        // Detached durable callers may only supply an ordinary positive wire ID. No sign-based temporary classification.
        ItemDetached(itemId, itemId <= int.MaxValue ? new(WiredVariableTarget.Furni, itemId, (int)itemId) : null);
    }
    private void ItemDetached(uint itemId, WiredVariableHolder? holder)
    {
        _metadata.Remove(itemId);
        _definitions.Remove(itemId); Module.DetachDefinition(itemId);
        if (holder is { } detached) HolderLeft(detached);
        FxDirty = true;
    }
    /// <summary>Only actual deletion of an owning item, never inventory pickup.</summary>
    public int DefinitionDeleted(uint itemId)
    {
        var removed = Module.DeleteDefinition(itemId); ItemDetached(itemId); return removed;
    }
    public IReadOnlyList<WiredVariableChange> DrainChanges()
    {
        var changes = Module.DrainChanges();
        if (changes.Count > 0) FxDirty = true;
        return changes;
    }
    public void InvalidateFx() => FxDirty = true;
    public void FxFlushed() => FxDirty = false;
}
