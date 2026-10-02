using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Only returns concrete executable scalar boxes. Registry descriptors remain unsupported until constructed.</summary>
public static class WiredVariableBoxFactory
{
    public static IWiredConfiguredItem? Create(Room room, Item item, WiredVariableModule variables, Func<long> nowMs)
    {
        if (item.Definition.WiredDescriptor is not { } descriptor
            || !WiredVariableExecutors.Supports(descriptor.CanonicalName)) return null;
        return new WiredVariableConfiguredBox(room, item, descriptor, new(variables, nowMs));
    }
}
