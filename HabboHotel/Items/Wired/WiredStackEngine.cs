using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Items.Wired;

// Room-owned dispatch, evaluation and scheduling. The lock also serializes legacy synchronous
// callbacks: context is scoped to a call, including events raised recursively by an action.
internal sealed partial class WiredStackEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<uint, IWiredItem> _items = new();
    private readonly Dictionary<(int X, int Y), IWiredItem[]> _stacks = new();
    private readonly PriorityQueue<ScheduledAction, (long Due, double Height, uint Id, long Firing)> _schedule = new();
    private readonly HashSet<ActionChain> _pending = new();
    private readonly Func<long> _now;
    private readonly Func<IWiredItem, bool> _attached;
    private readonly Func<object[], bool> _actorPresent;
    private readonly Func<object[], object?>? _actorVisit;
    private readonly Action<Item> _flash;
    private readonly Action<Exception> _error;
    private readonly WiredEngineLimits _limits;
    private readonly WiredEngineStats _stats = new();
    private WiredExecutionContext? _context;
    private int _passDepth;
    private int _remaining;
    private int _passPeakDepth;
    private bool _budgetDenied;
    private long _passStarted;
    private long _sequence;
    private bool _draining;
    private ScheduledAction? _executingAction;

    public WiredStackEngine(Func<long> now, Func<IWiredItem, bool> attached,
        Func<object[], bool> actorPresent, Action<Item> flash, Action<Exception> error,
        WiredEngineLimits? limits = null, Func<object[], object?>? actorVisit = null)
    {
        _now = now;
        _attached = attached;
        _actorPresent = actorPresent;
        _actorVisit = actorVisit;
        _flash = flash;
        _error = error;
        _limits = limits ?? new();
    }

    internal WiredEngineLimits Limits => _limits;
    // Raised under the engine lock; the handler must not call back into the engine.
    internal Action<WiredEngineLimit, string>? LimitReached
    {
        get; set;
    }
    // The queue is read live: removal, cancellation and Clear change it without ending a pass.
    internal WiredEngineWindow ReadStats()
    {
        lock (_sync)
        {
            return _stats.Read(_now(), PendingCount);
        }
    }

    public bool Add(IWiredItem box)
    {
        lock (_sync)
        {
            if (!_items.TryAdd(box.Item.Id, box))
            {
                return false;
            }

            if (box is IWiredTimedTrigger timer)
            {
                timer.Reset(_now());
            }

            UpdateRuntimeItems();

            return true;
        }
    }

    public bool Remove(uint id)
    {
        lock (_sync)
        {
            if (!_items.Remove(id, out var removed))
            {
                return false;
            }

            CancelPending(removed);
            ResetRuntimeTile(removed.Item.GetX, removed.Item.GetY);
            UpdateRuntimeItems();

            return true;
        }
    }

    public bool TryGet(uint id, [NotNullWhen(true)] out IWiredItem? box)
    {
        lock (_sync)
        {
            return _items.TryGetValue(id, out box);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            _stacks.Clear();

            foreach (var entry in _schedule.UnorderedItems.ToArray())
            {
                CancelAuxiliary(entry.Element);
            }

            _schedule.Clear();
            _pending.Clear();

            foreach (var dispatch in _dispatches)
            {
                dispatch.Current?.Dispose();
            }

            foreach (var dispatch in _dispatches)
            {
                dispatch.IsQueued = false;
            }

            _dispatches.Clear();
            _queuedSlots = 0;
            _runtimePositions.Clear();
            UpdateFastWork();
        }
    }

    // A successful publication cancels all firings that captured this stack.
    public void CancelPending(IWiredItem box)
    {
        lock (_sync)
        {
            if (_activeFiring?.Stack.Contains(box) == true)
            {
                _activeFiring.Cancelled = true;
            }

            _pending.RemoveWhere(chain => chain.Contains(box));
            PruneSchedule();
            var tile = (box.Item.GetX, box.Item.GetY);
            var kept = _dispatches.Where(pending => pending.Current?.Stack.Contains(box) != true
                && pending.Triggers?.Contains(box) != true
                && !pending.CapturedBoxes.Contains(box)
                && (pending.Signal == null || (pending.Signal.Receiver.GetX, pending.Signal.Receiver.GetY) != tile)).ToArray();

            foreach (var pending in _dispatches.Except(kept))
            {
                pending.Current?.Dispose();
            }

            foreach (var pending in _dispatches)
            {
                pending.IsQueued = false;
            }

            _dispatches.Clear();
            _queuedSlots = 0;

            foreach (var pending in kept)
            {
                QueueDispatch(pending);
            }

            UpdateFastWork();
        }
    }

    public ICollection<IWiredItem> GetBoxes(IWiredItem source, InteractionType kind) => Pass(() =>
        GetStack(source).Where(x => IsKind(x, kind)).ToList());

    public bool Dispatch(WiredBoxType type, params object[] arguments) => Pass(() =>
    {
        RefreshStacks();
        var context = CreateContext((arguments ?? []).ToArray(), _queuedDepth ?? (_runtimeContext?.Depth ?? _context?.Depth ?? -1) + 1);
        _queuedDepth = null;

        if (TooDeep(context.Depth))
        {
            return false;
        }

        var matched = false;

        // Each registered trigger is visited once, even when a tile has several of the same type.
        foreach (var trigger in _stacks.Values.SelectMany(x => x)
                     .Where(x => x is not IWiredContextualTrigger && x.Type == type && IsKind(x, InteractionType.WiredTrigger))
                     .OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).ToArray())
        {
            if (OutOfBudget())
            {
                break;
            }

            matched |= Execute(trigger, context);
        }

        return matched;
    });

    public bool RunStack(IWiredItem source, object[] arguments, Action? onAccepted = null) => Pass(() =>
        RunStackCore(source, CreateContext(arguments.ToArray(), _context?.Depth ?? 0), null, onAccepted));

    // Legacy repeaters gate each condition on any room actor independently, then run actorless
    // room actions once. This quantifier is intentional and covered separately from actor events.
    public bool RunPeriodicStack(IWiredItem source, object[][] actors) => Pass(() =>
        RunStackCore(source, CreateContext([], 0), actors, null));

    public bool CallStacks(IEnumerable<Item> targets, object[] arguments) => Pass(() =>
    {
        var depth = (_context?.Depth ?? 0) + 1;

        if (TooDeep(depth))
        {
            return false;
        }

        var matched = false;
        var visited = new HashSet<(int, int)>();

        foreach (var target in targets.OrderBy(x => x.GetZ).ThenBy(x => x.Id).ToArray())
        {
            if (!_items.TryGetValue(target.Id, out var box) || !IsAttached(box))
            {
                continue;
            }

            if (!visited.Add((target.GetX, target.GetY)))
            {
                continue;
            }

            matched |= RunStackCore(box, CreateContext(arguments.ToArray(), depth), null, null);
        }

        return matched;
    });

    public void OnCycle() => Pass(() =>
    {
        RefreshStacks();
        DrainDueActions();

        foreach (var box in _items.Values.ToArray())
        {
            // IWiredCycle remains the saved-delay contract; only periodic triggers tick.
            if (!IsAttached(box) || !IsKind(box, InteractionType.WiredTrigger) || box is not IWiredCycle cycle)
            {
                continue;
            }

            if (OutOfBudget())
            {
                break;
            }

            if (cycle.TickCount > 0)
            {
                cycle.TickCount--;
            }
            else
            {
                _remaining--;

                try
                {
                    cycle.OnCycle();
                }
                catch (Exception e) { _error(e); }
            }
        }

        if (_runtimeRoom != null)
        {
            RunRuntimeTimersAndSignals();
        }

        DrainDueActions();
        UpdateFastWork();

        return true;
    });

    private bool RunStackCore(IWiredItem source, WiredExecutionContext context,
        object[][]? conditionActors, Action? onAccepted)
    {
        var firedAt = _now();

        if (TooDeep(context.Depth) || !IsActorPresent(context))
        {
            return false;
        }

        var stack = GetStack(source);

        if (stack.Length == 0)
        {
            return false;
        }

        if (_runtimeRoom != null && stack.Any(x => x is IWiredConfiguredItem))
        {
            var typed = _runtimeContext?.Fork(_runtimeContext.Event, context.Depth)
                ?? CreateContext((_legacyRuntimeEvent ?? new(WiredEventKind.Periodic)) with
                {
                    Actor = context.ActorVisit as Plus.HabboHotel.Rooms.RoomUser ?? context.Arguments.FirstOrDefault() as Plus.HabboHotel.Rooms.RoomUser ?? _legacyRuntimeEvent?.Actor
                }, context.Depth);
            typed.Trigger = source;

            if (_runtimeContext == null)
            {
                SeedEvent(typed);
            }

            var accepted = RunRuntimeStack(source, typed, null, conditionActors, defer: onAccepted == null);

            if (accepted)
            {
                onAccepted?.Invoke();
            }

            return accepted;
        }

        var conditions = stack.Where(x => IsKind(x, InteractionType.WiredCondition)).ToArray();

        foreach (var condition in conditions)
        {
            if (OutOfBudget())
            {
                return false;
            }

            var passed = conditionActors == null
                ? Execute(condition, context)
                : conditionActors.Any(actor => Execute(condition, CreateContext(actor, context.Depth)));

            if (!passed)
            {
                return false;
            }

            Flash(condition);
        }

        var addons = stack.Where(x => x.Type == WiredBoxType.AddonRandomEffect).ToArray();
        var actions = stack.Where(x => IsKind(x, InteractionType.WiredEffect)
                                      && x.Type != WiredBoxType.AddonRandomEffect).ToArray();

        if (addons.Length > 0)
        {
            foreach (var addon in addons)
            {
                Flash(addon);
            }

            actions = actions.Length == 0 ? [] : [actions[Random.Shared.Next(actions.Length)]];
        }

        if (!CanSchedule(actions))
        {
            return false;
        }

        onAccepted?.Invoke();
        Flash(source);

        // This is synchronous stack acceptance (including chat consumption), not action success.
        return ScheduleActions(source, stack, actions, context, firedAt: firedAt);
    }

    private bool CanSchedule(IWiredItem[] actions, bool prepared = false)
    {
        if (actions.Length > 0 && PendingCount >= _limits.MaxPendingStacks
            && _pending.RemoveWhere(chain => !IsChainValid(chain)) > 0)
        {
            PruneSchedule();
        }

        if (actions.Length > 0 && QueueFull(1))
        {
            return false;
        }

        // Preparation must happen synchronously; reject before acceptance if its calls cannot fit.
        if (prepared)
        {
            return true;
        }

        var preparations = actions.Count(action => action is IWiredFiringPreparation);

        if (preparations <= _remaining)
        {
            return true;
        }

        LimitReached?.Invoke(WiredEngineLimit.ExecutionBudget,
            $"A stack needed {preparations} executions to prepare but only {_remaining} were left in this pass, so it did not fire.");

        return false;
    }

    private bool IsChainValid(ActionChain chain) => IsAttached(chain.Source)
        && chain.Source.Item.MovementGeneration == chain.MovementGeneration
        && (chain.Source.Item.GetX, chain.Source.Item.GetY) == chain.Tile
        && IsActorPresent(chain.Context);

    // Shared by stack pipelines inside a room-engine pass. Delay is captured per firing.
    internal bool ScheduleActions(IWiredItem source, IWiredItem[] capturedStack, IWiredItem[] actions,
        WiredExecutionContext context, Func<IWiredItem, long>? delayMilliseconds = null, long? firedAt = null, bool prepared = false)
    {
        if (!CanSchedule(actions, prepared))
        {
            return false;
        }

        if (actions.Length == 0)
        {
            return true;
        }

        var chain = new ActionChain(source, capturedStack, context, actions.Length);
        var firing = ++_sequence;
        var startedAt = firedAt ?? _now();
        _pending.Add(chain);

        foreach (var action in actions)
        {
            if (!_pending.Contains(chain))
            {
                break;
            }

            var due = startedAt + Math.Max(0, (delayMilliseconds ?? GetDelay)(action));

            if (!prepared && action is IWiredFiringPreparation preparation
                && !Invoke(action, context, () => preparation.Prepare(context.Arguments)))
            {
                if (--chain.Remaining == 0)
                {
                    _pending.Remove(chain);
                }

                continue;
            }

            var scheduled = new ScheduledAction(chain, action);
            _schedule.Enqueue(scheduled, (due, action.Item.GetZ, action.Item.Id, firing));
        }

        DrainDueActions();

        return true;
    }

    private void PruneSchedule()
    {
        var entries = _schedule.UnorderedItems.ToArray();
        var kept = entries.Where(x => _pending.Contains(x.Element.Chain)).ToArray();
        _schedule.Clear();

        foreach (var entry in kept)
        {
            _schedule.Enqueue(entry.Element, entry.Priority);
        }

        foreach (var entry in entries.Where(x => !_pending.Contains(x.Element.Chain)))
        {
            CancelAuxiliary(entry.Element);
        }
    }

    private void DrainDueActions()
    {
        // Nested stack calls enqueue their actions, and the active drain keeps global ordering.
        if (_draining)
        {
            return;
        }

        _draining = true;
        var prune = false;

        try
        {
            while (_schedule.TryPeek(out _, out var priority) && priority.Due <= _now() && !OutOfBudget())
            {
                var scheduled = _schedule.Dequeue();
                var chain = scheduled.Chain;

                if (!_pending.Contains(chain))
                {
                    CancelAuxiliary(scheduled);
                    continue;
                }

                if (!IsChainValid(chain))
                {
                    _pending.Remove(chain);
                    CancelAuxiliary(scheduled);
                    prune = true;
                    continue;
                }

                var action = scheduled.Box;

                try
                {
                    if (!IsAttached(action) || (action.Item.GetX, action.Item.GetY) != chain.Tile)
                    {
                        CancelAuxiliary(scheduled);
                        continue;
                    }

                    scheduled.Finished = true;
                    var previousAction = _executingAction;
                    _executingAction = scheduled;

                    try
                    {
                        var succeeded = scheduled.Callback == null ? Execute(action, chain.Context)
                            : Invoke(action, chain.Context, () => { scheduled.Callback(); return true; });

                        if (succeeded && chain.Context.Runtime?.Policy.StopOnSuccess == true)
                        {
                            _pending.Remove(chain);
                            prune = true;
                        }
                    }
                    finally { _executingAction = previousAction; }

                    Flash(action);
                }
                finally
                {
                    if (--chain.Remaining == 0)
                    {
                        _pending.Remove(chain);
                    }
                }
            }
        }
        finally
        {
            if (prune)
            {
                PruneSchedule();
            }

            _draining = false;
        }
    }

    private static long GetDelay(IWiredItem action) => action is IWiredActionDelay intrinsic
        ? Math.Max(0, intrinsic.DelayMilliseconds)
        : action is IWiredCycle cycle ? Math.Max(0L, cycle.Delay) * 500 : 0;

    private void Flash(IWiredItem box)
    {
        try
        {
            _flash(box.Item);
        }
        catch (Exception e) { _error(e); }
    }

    private WiredExecutionContext CreateContext(object[] arguments, int depth) =>
        new(arguments, depth, _actorVisit?.Invoke(arguments));

    private bool IsActorPresent(WiredExecutionContext context) => context.Runtime is { } runtime
        ? runtime.Event.Kind == WiredEventKind.Leave || runtime.Event.Actor == null
            || runtime.Targets.ResolveUsers(runtime, [runtime.Event.Actor.VirtualId], WiredSources.Selected, raw: true)
                .Contains(runtime.Event.Actor)
        : _actorPresent(context.Arguments)
            && (_actorVisit == null || ReferenceEquals(context.ActorVisit, _actorVisit(context.Arguments)));

    private bool Execute(IWiredItem box, WiredExecutionContext context) =>
        Invoke(box, context, () => context.Runtime is { } runtime
            ? ExecuteRuntimeBody(box, runtime) : box.Execute(context.Arguments));

    private bool Invoke(IWiredItem box, WiredExecutionContext context, Func<bool> body)
    {
        if (OutOfBudget() || !IsAttached(box) || !IsActorPresent(context))
        {
            return false;
        }

        _remaining--;
        _passPeakDepth = Math.Max(_passPeakDepth, context.Depth);
        var previous = _context;
        _context = context;
        var previousRuntime = _runtimeContext;
        _runtimeContext = context.Runtime;

        if (context.Runtime != null)
        {
            context.Runtime.NowMilliseconds = _now();
        }

        try
        {
            return body();
        }
        catch (Exception e) { _error(e); return false; }
        finally { _context = previous; _runtimeContext = previousRuntime; }
    }

    private bool IsAttached(IWiredItem box) => _items.TryGetValue(box.Item.Id, out var registered)
        && ReferenceEquals(registered, box) && _attached(box);

    private static bool IsKind(IWiredItem box, InteractionType kind) => box is IWiredConfiguredItem configured
        ? RuntimeSupported(configured) && (kind, configured.Descriptor.Category) switch
        {
            (InteractionType.WiredTrigger, WiredBoxCategory.Trigger) => true,
            (InteractionType.WiredCondition, WiredBoxCategory.Condition) => true,
            (InteractionType.WiredEffect, WiredBoxCategory.Action) => true,
            _ => false
        }
        : box.Item.Definition.InteractionType == kind;

    private IWiredItem[] GetStack(IWiredItem source)
    {
        RefreshStacks();

        return IsAttached(source) && _stacks.TryGetValue((source.Item.GetX, source.Item.GetY), out var stack)
            ? stack : [];
    }

    private void RefreshStacks()
    {
        // Item coordinates can change through rollers and movement without a wired hook.
        // Rebuild the tile index at the seam, so callers never have to remember invalidation.
        foreach (var box in _items.Values.ToArray())
        {
            if (!_attached(box))
            {
                Remove(box.Item.Id);
            }
        }

        _stacks.Clear();

        foreach (var group in _items.Values.GroupBy(x => (x.Item.GetX, x.Item.GetY)))
        {
            _stacks[group.Key] = group.OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).ToArray();
        }

        UpdateRuntimeItems();
    }

    private T Pass<T>(Func<T> body)
    {
        lock (_sync)
        {
            if (_passDepth++ == 0)
            {
                _remaining = _limits.MaxExecutionsPerPass;
                _passPeakDepth = 0;
                _budgetDenied = false;
                _passStarted = Stopwatch.GetTimestamp();
            }

            try
            {
                return body();
            }
            finally
            {
                if (--_passDepth == 0)
                {
                    EndPass();
                }
            }
        }
    }

    // Observes the pass that just ended. It reports the limits the pass met and changes none of its decisions.
    private void EndPass()
    {
        var executions = _limits.MaxExecutionsPerPass - Math.Max(0, _remaining);

        if (_budgetDenied)
        {
            LimitReached?.Invoke(WiredEngineLimit.ExecutionBudget,
                $"One pass used all {_limits.MaxExecutionsPerPass} wired executions; the rest waits for the next pass or was dropped.");
        }

        _stats.Record(_now(), Stopwatch.GetElapsedTime(_passStarted).TotalMilliseconds, executions, _passPeakDepth);
    }

    // Every budget check that turns waiting work away goes through here, so a pass that spends its
    // budget exactly is not reported as capped.
    private bool OutOfBudget()
    {
        if (_remaining > 0)
        {
            return false;
        }

        _budgetDenied = true;

        return true;
    }

    private bool TooDeep(int depth)
    {
        if (depth <= _limits.MaxDepth)
        {
            return false;
        }

        LimitReached?.Invoke(WiredEngineLimit.Depth, $"A chain reached depth {depth}, over the limit of {_limits.MaxDepth}, and was not run.");

        return true;
    }

    private bool QueueFull(int slots)
    {
        if (PendingCount + slots <= _limits.MaxPendingStacks)
        {
            return false;
        }

        LimitReached?.Invoke(WiredEngineLimit.PendingStacks,
            $"The wait queue was full ({PendingCount} of {_limits.MaxPendingStacks} chains); new work was dropped.");

        return true;
    }

    private void CancelAuxiliary(ScheduledAction action)
    {
        if (action.Finished || action.OnCancelled == null)
        {
            return;
        }

        action.Finished = true;

        try
        {
            action.OnCancelled();
        }
        catch (Exception error) { _error(error); }
    }

    private sealed record ScheduledAction(ActionChain Chain, IWiredItem Box, Action? Callback = null, Action? OnCancelled = null)
    {
        public bool Finished
        {
            get; set;
        }
    }

    private sealed class ActionChain(IWiredItem source, IWiredItem[] stack,
        WiredExecutionContext context, int remaining)
    {
        public IWiredItem Source { get; } = source;
        public IWiredItem[] Stack { get; } = stack;
        public (int, int) Tile { get; } = (source.Item.GetX, source.Item.GetY);
        public long MovementGeneration { get; } = source.Item.MovementGeneration;
        public WiredExecutionContext Context { get; } = context;
        public int Remaining { get; set; } = remaining;
        public bool Contains(IWiredItem box) => stack.Contains(box);
    }
}
