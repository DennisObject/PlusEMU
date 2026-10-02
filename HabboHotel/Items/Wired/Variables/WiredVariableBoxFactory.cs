using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Returns concrete scalar executors or passive definitions backed by the room variable module.</summary>
public static class WiredVariableBoxFactory
{
    public static IWiredConfiguredItem? Create(Room room, Item item, WiredVariableModule variables, Func<long> nowMs,
        WiredVariableConfigurationPersistence? definitions = null)
    {
        if (item.Definition.WiredDescriptor is not { } descriptor) return null;
        if (WiredVariableDefinitions.Supports(descriptor.CanonicalName)) return definitions is null ? null
            : new WiredVariableDefinitionBox(room, item, descriptor, definitions, new(variables));
        return WiredVariableExecutors.Supports(descriptor.CanonicalName)
            ? new WiredVariableConfiguredBox(room, item, descriptor, new(variables, nowMs)) : null;
    }
}
