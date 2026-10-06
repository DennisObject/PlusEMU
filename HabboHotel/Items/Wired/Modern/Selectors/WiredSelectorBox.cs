using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

public sealed class WiredSelectorBox : WiredConfiguredBehaviorBox, IWiredContextualSelector
{
    private readonly WiredSelectorRoomState _state;
    private readonly IGroupManager _groups;
    private readonly Func<WiredRuntimeContext, WiredSelectorVariableQueries>? _variables;
    private readonly Func<WiredRuntimeContext, WiredSelectorWorld>? _readWorld;

    public WiredSelectorBox(Room room, Item item, WiredBoxDescriptor descriptor, WiredSelectorRoomState state,
        IGroupManager groups,
        Func<WiredRuntimeContext, WiredSelectorVariableQueries>? variables = null,
        Func<WiredRuntimeContext, WiredSelectorWorld>? readWorld = null)
        : base(room, item, descriptor, c => WiredSelectorConfiguration.Normalize(descriptor.CanonicalName, c))
    {
        _state = state;
        _groups = groups;
        _variables = variables;
        _readWorld = readWorld;

        if (descriptor.CanonicalName.EndsWith("_with_var", StringComparison.Ordinal) && variables is null)
        {
            throw new ArgumentException("Variable selectors require the concrete variable query provider", nameof(variables));
        }
    }

    public Runtime.WiredSelectorResult Select(WiredRuntimeContext context)
    {
        var configuration = context.ConfigurationOf(this);
        var input = WiredSelectorRuntimeInput.Capture(context, _state, _groups, readWorld: _readWorld);
        using var variables = WiredSelectorVariableBridge.RequiresQueries(Descriptor.CanonicalName, configuration, input.World)
            ? _variables?.Invoke(context) : null;
        input = input.WithVariables(variables);
        var raw = WiredSelectorModule.SelectRaw(Descriptor.CanonicalName, configuration, input.World, input.Selection);
        var kind = raw.Target switch
        {
            WiredSelectorTarget.Furni => WiredSelectionKind.Furni,
            WiredSelectorTarget.User => WiredSelectionKind.Users,
            _ => WiredSelectionKind.Both
        };

        return new(new(raw.Selection.FurniIds, raw.Selection.UserIds), kind, raw.FiltersExisting, raw.Invert);
    }
}

public static class WiredSelectorFactory
{
    public static IWiredContextualSelector? Create(Room room, Item item, WiredSelectorRoomState state,
        IGroupManager groups,
        Func<WiredRuntimeContext, WiredSelectorVariableQueries>? variables = null,
        Func<WiredRuntimeContext, WiredSelectorWorld>? readWorld = null)
    {
        var descriptor = item.Definition.WiredDescriptor;

        if (descriptor?.Category != WiredBoxCategory.Selector)
        {
            return null;
        }

        if (descriptor.CanonicalName.EndsWith("_with_var", StringComparison.Ordinal) && variables is null)
        {
            return null;
        }

        return new WiredSelectorBox(room, item, descriptor, state, groups, variables, readWorld);
    }
}
