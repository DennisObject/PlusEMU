using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public interface IWiredContextualItem : IWiredConfiguredItem
{
    bool Execute(WiredRuntimeContext context);
}

public interface IWiredContextualAction : IWiredContextualItem
{
    bool IsNegative { get; }
}

// Trigger Execute is a predicate; the engine alone evaluates and schedules its stack.
public interface IWiredContextualTrigger : IWiredContextualItem
{
    IReadOnlyCollection<WiredEventKind> Events { get; }
    bool HidesChat(WiredRuntimeContext context);
    // Execute is the pre-filter; this runs once the stack's selectors have filled the pool (Turbo's CanTriggerAsync).
    bool CanTrigger(WiredRuntimeContext context) => true;
}

public interface IWiredClickTrigger : IWiredContextualTrigger
{
    (bool BlockMenu, bool DoNotRotate) ClickSettings(WiredRuntimeContext context);
}

public interface IWiredContextualSelector : IWiredConfiguredItem
{
    WiredSelectorResult Select(WiredRuntimeContext context);
}

public interface IWiredContextualAddon : IWiredConfiguredItem
{
    // False applies before conditions. The execution limit uses that, so a failed condition spends its slot.
    bool AfterConditions { get; }
    bool Apply(WiredRuntimeContext context);
    void Reset();
}

public interface IWiredTimedTrigger : IWiredContextualTrigger
{
    // Return at most one actual event per poll; never emit a catch-up burst.
    WiredRuntimeEvent? Poll(long nowMilliseconds);
    // The box was placed, saved or moved: start over, period included.
    void Reset(long nowMilliseconds);
    // The room timer was reset: count elapsed time from now again. A repeater keeps its period.
    void ResetElapsed(long nowMilliseconds);
}

public interface IWiredRuntimeOperations
{
    bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false);
    bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false);
    void ResetTimers(IEnumerable<Item> targets);
}
