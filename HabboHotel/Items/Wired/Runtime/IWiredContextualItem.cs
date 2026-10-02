using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public interface IWiredContextualItem : IWiredConfiguredItem
{
    bool Execute(WiredRuntimeContext context);
}

// Trigger Execute is a predicate; the engine alone evaluates and schedules its stack.
public interface IWiredContextualTrigger : IWiredContextualItem
{
    IReadOnlyCollection<WiredEventKind> Events { get; }
    bool HidesChat(WiredRuntimeContext context);
}

public interface IWiredContextualSelector : IWiredConfiguredItem
{
    WiredSelectorResult Select(WiredRuntimeContext context);
}

public interface IWiredContextualAddon : IWiredConfiguredItem
{
    bool Apply(WiredRuntimeContext context);
    void Reset();
}

public interface IWiredTimedTrigger : IWiredContextualTrigger
{
    // Return at most one actual event per poll; never emit a catch-up burst.
    WiredRuntimeEvent? Poll(long nowMilliseconds);
    void Reset(long nowMilliseconds);
}

public interface IWiredRuntimeOperations
{
    bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false);
    bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false);
    void ResetTimers(IEnumerable<Item> targets);
}
