using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

public sealed class WiredAddonBox : WiredConfiguredBehaviorBox, IWiredContextualAddon
{
    private readonly WiredAddonModule _module;
    private readonly WiredSelectorRoomState _state;
    private readonly IGroupManager _groups;
    private readonly Func<WiredRuntimeContext, WiredSelectorVariableQueries>? _variables;
    private readonly Func<WiredRuntimeContext, WiredSelectorWorld>? _readWorld;

    public WiredAddonBox(Room room, Item item, WiredBoxDescriptor descriptor, WiredSelectorRoomState state,
        IGroupManager groups,
        Func<WiredRuntimeContext, WiredSelectorVariableQueries>? variables = null,
        Func<WiredRuntimeContext, WiredSelectorWorld>? readWorld = null)
        : base(room, item, descriptor, c => WiredAddonConfiguration.Normalize(descriptor.CanonicalName, c))
    {
        _module = new(descriptor.CanonicalName, Configuration);
        _state = state;
        _groups = groups;
        _variables = variables;
        _readWorld = readWorld;
    }

    public bool AfterConditions => Descriptor.CanonicalName == "wf_xtra_execution_limit";

    public bool Apply(WiredRuntimeContext context)
    {
        var configuration = context.ConfigurationOf(this);
        var needsVariables = Descriptor.CanonicalName switch
        {
            "wf_xtra_mov_curve" => WiredSelectorSources.Param(configuration, 3) == 1,
            "wf_xtra_rotate_to_dir" => WiredSelectorSources.Param(configuration, 14) != 0
                && WiredSelectorSources.Param(configuration, 15) == 1,
            _ => false
        };
        using var variables = needsVariables ? _variables?.Invoke(context) : null;
        var input = WiredSelectorRuntimeInput.Capture(context, _state, _groups, variables, _readWorld);
        var before = context.Policy.Addons.TextFormatters.Count;
        var passed = _module.Apply(input.ForAddons(context.NowMilliseconds), context.Policy.Addons, configuration);

        // The shared ordered list also contains formatters owned by the variable module.
        foreach (var formatter in context.Policy.Addons.TextFormatters.Skip(before))
        {
            context.Policy.TextFormatters.Add((current, text) => formatter(
                WiredSelectorRuntimeInput.Capture(current, _state, _groups, readWorld: _readWorld).ForAddons(current.NowMilliseconds), text));
        }

        return passed;
    }

    protected override void ConfigurationChanged() => _module.Configure(Configuration);
    public void Reset() => _module.Reset();
}

public static class WiredAddonFactory
{
    public static IWiredContextualAddon? Create(Room room, Item item, WiredSelectorRoomState state,
        IGroupManager groups,
        Func<WiredRuntimeContext, WiredSelectorVariableQueries>? variables = null,
        Func<WiredRuntimeContext, WiredSelectorWorld>? readWorld = null)
    {
        var descriptor = item.Definition.WiredDescriptor;

        if (descriptor?.Category != WiredBoxCategory.Addon || !WiredAddonModule.Names.Contains(descriptor.CanonicalName))
        {
            return null;
        }

        return new WiredAddonBox(room, item, descriptor, state, groups, variables, readWorld);
    }
}
