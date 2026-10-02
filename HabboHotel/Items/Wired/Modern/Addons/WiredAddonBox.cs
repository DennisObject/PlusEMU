using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

public sealed class WiredAddonBox : WiredConfiguredBehaviorBox, IWiredContextualAddon
{
    private readonly WiredAddonModule _module;
    private readonly WiredSelectorRoomState _state;
    private readonly Func<WiredRuntimeContext, WiredSelectorVariableQueries>? _variables;

    public WiredAddonBox(Room room, Item item, WiredBoxDescriptor descriptor, WiredSelectorRoomState state,
        Func<WiredRuntimeContext, WiredSelectorVariableQueries>? variables = null)
        : base(room, item, descriptor, c => WiredAddonConfiguration.Normalize(descriptor.CanonicalName, c))
    {
        _module = new(descriptor.CanonicalName, Configuration);
        _state = state;
        _variables = variables;
    }

    public bool AfterConditions => Descriptor.CanonicalName == "wf_xtra_execution_limit";

    public bool Apply(WiredRuntimeContext context)
    {
        using var variables = _variables?.Invoke(context);
        var input = WiredSelectorRuntimeInput.Capture(context, _state, variables);
        var before = context.Policy.Addons.TextFormatters.Count;
        var passed = _module.Apply(input.ForAddons(context.NowMilliseconds), context.Policy.Addons, context.ConfigurationOf(this));
        // The shared ordered list also contains formatters owned by the variable module.
        foreach (var formatter in context.Policy.Addons.TextFormatters.Skip(before))
            context.Policy.TextFormatters.Add((current, text) => formatter(
                WiredSelectorRuntimeInput.Capture(current, _state).ForAddons(current.NowMilliseconds), text));
        return passed;
    }

    protected override void ConfigurationChanged() => _module.Configure(Configuration);
    public void Reset() => _module.Reset();
}

public static class WiredAddonFactory
{
    public static IWiredContextualAddon? Create(Room room, Item item, WiredSelectorRoomState state,
        Func<WiredRuntimeContext, WiredSelectorVariableQueries>? variables = null)
    {
        var descriptor = item.Definition.WiredDescriptor;
        if (descriptor?.Category != WiredBoxCategory.Addon || !WiredAddonModule.Names.Contains(descriptor.CanonicalName)) return null;
        return new WiredAddonBox(room, item, descriptor, state, variables);
    }
}
