using Plus.HabboHotel.Items.Wired.Modern.Addons;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public enum WiredConditionMode
{
    All, Any, None, NoneMatch, NotAll, LessThan, Exactly, MoreThan
}

public sealed class WiredExecutionPolicy
{
    public WiredConditionMode ConditionMode { get; set; }
    public int ConditionThreshold { get; set; }
    public bool OrderedEffects { get; set; }
    public bool StopOnSuccess { get; set; }
    public long DelayMilliseconds { get; set; }
    public WiredAddonPolicy Addons { get; } = new();
    // Stateful random/unseen boxes own the picker. The engine invokes it only after conditions.
    public Func<IReadOnlyList<IWiredItem>, IReadOnlyList<IWiredItem>>? ChooseActions { get; set; }
    public List<Func<WiredRuntimeContext, string, string>> TextFormatters { get; } = [];
    public string FormatText(WiredRuntimeContext context, string text) =>
        TextFormatters.Aggregate(text, (current, formatter) => formatter(context, current));
}
