using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using WiredSelectorResult = Plus.HabboHotel.Items.Wired.Runtime.WiredSelectorResult;

namespace Plus.HabboHotel.Items.Wired;

internal sealed partial class WiredStackEngine
{
    private Room? _runtimeRoom;
    private WiredTargetResolver? _targets;
    private IWiredRuntimeOperations? _operations;
    private WiredRuntimeContext? _runtimeContext;
    private WiredRuntimeEvent? _legacyRuntimeEvent;
    public Action<WiredRuntimeEvent, long>? ObserveEvent { get; set; }
    private readonly Queue<PendingSignal> _signals = new();
    private readonly Dictionary<uint, (int X, int Y, double Z)> _runtimePositions = [];
    private int _fastWork;
    private Action<bool>? _fastWorkObserver;
    private long _lastTimerPoll = -1;

    public void BindRuntime(Room room, WiredTargetResolver targets, IWiredRuntimeOperations operations)
    {
        lock (_sync) { _runtimeRoom = room; _targets = targets; _operations = operations; }
    }

    public bool NeedsFastCycle => Volatile.Read(ref _fastWork) != 0;

    public void ObserveFastWork(Action<bool>? observer)
    {
        lock (_sync) { _fastWorkObserver = observer; observer?.Invoke(NeedsFastCycle); }
    }

    public bool Dispatch(WiredRuntimeEvent @event) => Pass(() =>
    {
        if (_runtimeRoom == null) return false;
        RefreshStacks();
        ObserveEvent?.Invoke(@event, _now());
        var root = CreateContext(@event, (_runtimeContext?.Depth ?? _context?.Depth ?? -1) + 1);
        if (root.Depth > _limits.MaxDepth) return false;
        var accepted = false;
        foreach (var trigger in _items.Values.OfType<IWiredContextualTrigger>()
                     .Where(x => RuntimeSupported(x) && x.Events.Contains(@event.Kind))
                     .OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).ToArray())
        {
            if (_remaining <= 0) break;
            var context = root.Fork(@event, root.Depth);
            context.Trigger = trigger;
            SeedEvent(context);
            context.Capture(GetStack(trigger));
            if (!InvokeRuntime(trigger, context, () => trigger.Execute(context))) continue;
            if (!RunRuntimeStack(trigger, context, null)) continue;
            accepted |= @event.Kind != WiredEventKind.Speech || trigger.HidesChat(context);
        }
        UpdateFastWork();
        return accepted;
    });

    public bool DispatchLegacy(WiredBoxType type, WiredRuntimeEvent? typed, object[] arguments) => Pass(() =>
    {
        var previous = _legacyRuntimeEvent;
        _legacyRuntimeEvent = typed;
        try
        {
            var accepted = Dispatch(type, arguments);
            if (typed != null) accepted |= Dispatch(typed);
            return accepted;
        }
        finally { _legacyRuntimeEvent = previous; }
    });

    public bool CallStacks(WiredRuntimeContext parent, IEnumerable<Item> targets, bool negative = false) => Pass(() =>
    {
        if (!ReferenceEquals(parent.Room, _runtimeRoom) || parent.Depth >= _limits.MaxDepth) return false;
        var accepted = false;
        var visited = new HashSet<(int, int)>();
        foreach (var item in targets.OrderBy(x => x.GetZ).ThenBy(x => x.Id).ToArray())
        {
            if (_remaining <= 0) break;
            if (!_items.TryGetValue(item.Id, out var source) || !ReferenceEquals(source.Item, item) || !IsAttached(source)) continue;
            if (!visited.Add((item.GetX, item.GetY))) continue;
            var child = parent.Fork(parent.Event, parent.Depth + 1);
            child.Triggering = parent.Selected.Copy();
            child.Selected = child.Triggering.Copy();
            accepted |= RunRuntimeStack(source, child, negative);
        }
        UpdateFastWork();
        return accepted;
    });

    public bool SendSignal(WiredRuntimeContext parent, IEnumerable<Item> receivers,
        WiredSelection selection, bool negative = false) => Pass(() =>
    {
        if (!ReferenceEquals(parent.Room, _runtimeRoom) || parent.Depth >= _limits.MaxDepth) return false;
        var live = _targets!.AllFurni().ToDictionary(x => x.Id);
        var accepted = false;
        foreach (var receiver in receivers.DistinctBy(x => x.Id))
        {
            if (_remaining <= 0 || _signals.Count + _pending.Count >= _limits.MaxPendingStacks) break;
            if (!live.TryGetValue(receiver.Id, out var attached) || !ReferenceEquals(receiver, attached)
                || !string.Equals(receiver.Definition.InteractionName, "antenna", StringComparison.OrdinalIgnoreCase)) continue;
            _remaining--;
            var child = parent.Fork(new(WiredEventKind.Signal) { EventItem = receiver, Code = unchecked((int)receiver.Id) }, parent.Depth + 1);
            child.Signal = new(selection, parent.Values);
            child.Triggering = selection.Copy();
            child.Selected = selection.Copy();
            _signals.Enqueue(new(receiver, receiver.MovementGeneration, child, negative));
            accepted = true;
        }
        UpdateFastWork();
        return accepted;
    });

    // Auxiliary animation work belongs to the actual action firing and shares its cancellation.
    public bool ScheduleAux(WiredRuntimeContext context, int delayMilliseconds, Action callback, Action? onCancelled = null) => Pass(() =>
    {
        var active = _executingAction;
        if (active == null || !ReferenceEquals(active.Chain.Context.Runtime, context)
            || !CanSchedule([active.Box])) return false;
        var chain = new ActionChain(active.Chain.Source, active.Chain.Stack, active.Chain.Context, 1);
        _pending.Add(chain);
        _schedule.Enqueue(new(chain, active.Box, callback, onCancelled),
            (_now() + Math.Max(0, delayMilliseconds), active.Box.Item.GetZ, active.Box.Item.Id, ++_sequence));
        UpdateFastWork();
        return true;
    });

    public void ResetTimers(IEnumerable<Item> targets) => Pass(() =>
    {
        var visited = new HashSet<uint>();
        foreach (var target in targets)
        {
            if (!_items.TryGetValue(target.Id, out var source) || !ReferenceEquals(source.Item, target)) continue;
            foreach (var box in GetStack(source).Where(x => visited.Add(x.Item.Id)))
            {
                if (box is IWiredTimedTrigger timer) timer.Reset(_now());
                else if (box is IWiredCycle cycle && IsKind(box, InteractionType.WiredTrigger)) cycle.TickCount = cycle.Delay;
            }
        }
        return true;
    });

    public bool PublishConfigured(IWiredConfiguredItem original, WiredConfiguration validated, Action persist) => Pass(() =>
    {
        if (!IsAttached(original) || !RuntimeSupported(original)) return false;
        persist();
        CancelPending(original);
        original.ApplyConfiguration(validated);
        ResetRuntimeTile(original.Item.GetX, original.Item.GetY);
        UpdateFastWork();
        return true;
    });

    public bool PublishPromotion(IWiredItem original, IWiredConfiguredItem candidate,
        WiredConfiguration validated, Action persist) => Pass(() =>
    {
        if (!IsAttached(original) || !ReferenceEquals(original.Item, candidate.Item) || !RuntimeSupported(candidate)) return false;
        persist();
        candidate.ApplyConfiguration(validated);
        CancelPending(original);
        _items[original.Item.Id] = candidate;
        ResetRuntimeTile(original.Item.GetX, original.Item.GetY);
        RefreshStacks();
        return true;
    });

    public bool PublishLegacy(IWiredItem original, IWiredItem candidate, Action persist) => Pass(() =>
    {
        if (!IsAttached(original) || original.Type != candidate.Type || original.Item.Id != candidate.Item.Id) return false;
        persist();
        CancelPending(original);
        original.StringData = candidate.StringData;
        original.BoolData = candidate.BoolData;
        original.ItemsData = candidate.ItemsData;
        original.SetItems = new(candidate.SetItems);
        if (original is IWiredCycle cycle && candidate is IWiredCycle prepared) cycle.Delay = prepared.Delay;
        ResetRuntimeTile(original.Item.GetX, original.Item.GetY);
        return true;
    });

    public void OnFastCycle() => Pass(() =>
    {
        if (_runtimeRoom == null || !NeedsFastCycle) return false;
        RefreshStacks();
        RunRuntimeTimersAndSignals();
        DrainDueActions();
        UpdateFastWork();
        return true;
    });

    private WiredRuntimeContext CreateContext(WiredRuntimeEvent @event, int depth) =>
        new(_runtimeRoom!, @event, _targets!, _operations!) { Depth = depth, NowMilliseconds = _now() };

    private static void SeedEvent(WiredRuntimeContext context)
    {
        if (context.Event.EventItem != null) context.Triggering.FurniIds.Add(context.Event.EventItem.Id);
        if (context.Event.Actor != null) context.Triggering.UserIds.Add(context.Event.Actor.VirtualId);
        context.Selected = context.Triggering.Copy();
    }

    private bool RunRuntimeStack(IWiredItem source, WiredRuntimeContext context, bool? negative)
    {
        if (context.Depth > _limits.MaxDepth || !IsAttached(source)) return false;
        var stack = GetStack(source);
        context.Capture(stack);
        foreach (var selector in stack.OfType<IWiredContextualSelector>().Where(RuntimeSupported))
        {
            WiredSelectorResult? selected = null;
            if (!InvokeRuntime(selector, context, () => { selected = selector.Select(context); return true; })) return false;
            ComposeSelector(context, selected!);
        }
        context.Selected = new(
            context.SelectorKinds.HasFlag(WiredSelectionKind.Furni) ? context.SelectorPool.FurniIds : context.Triggering.FurniIds,
            context.SelectorKinds.HasFlag(WiredSelectionKind.Users) ? context.SelectorPool.UserIds : context.Triggering.UserIds);
        var addons = stack.OfType<IWiredContextualAddon>().Where(RuntimeSupported).ToArray();
        foreach (var addon in addons.Where(x => !x.AfterConditions).OrderBy(x => x.Descriptor.CanonicalName is "wf_xtra_filter_furni" or "wf_xtra_filter_users" ? 0 : 1))
            if (!InvokeRuntime(addon, context, () => addon.Apply(context))) return false;
        var filtered = new WiredSelectedIds();
        filtered.FurniIds.UnionWith(context.Selected.FurniIds);
        filtered.UserIds.UnionWith(context.Selected.UserIds);
        filtered = context.Policy.Addons.FilterSelection(filtered, Random.Shared);
        context.Selected = new(filtered.FurniIds, filtered.UserIds);
        if (context.SelectorKinds.HasFlag(WiredSelectionKind.Furni))
        { context.SelectorPool.FurniIds.Clear(); context.SelectorPool.FurniIds.UnionWith(filtered.FurniIds); }
        if (context.SelectorKinds.HasFlag(WiredSelectionKind.Users))
        { context.SelectorPool.UserIds.Clear(); context.SelectorPool.UserIds.UnionWith(filtered.UserIds); }
        var conditions = stack.Where(x => IsKind(x, InteractionType.WiredCondition)).ToArray();
        var scoped = context.Policy.Addons.Conditions;
        var grouped = scoped == null ? [] : conditions.Where(c => scoped.ConditionIds.Contains(c.Item.Id)).ToArray();
        var ordinary = scoped == null ? conditions : conditions.Except(grouped).ToArray();
        var matched = 0;
        foreach (var condition in ordinary)
        {
            if (_remaining <= 0) return false;
            if (InvokeRuntime(condition, context, () => ExecuteRuntimeBody(condition, context))) matched++;
        }
        var passed = MatchConditions(context.Policy, matched, ordinary.Length);
        if (scoped != null)
        {
            matched = 0;
            foreach (var condition in grouped)
            {
                if (_remaining <= 0) return false;
                if (InvokeRuntime(condition, context, () => ExecuteRuntimeBody(condition, context))) matched++;
            }
            passed &= WiredConditionPolicyEvaluator.Matches(scoped.Mode, matched, grouped.Length, scoped.Count);
        }
        if (negative is { } requestedNegative && (requestedNegative ? passed : !passed)) return false;
        var actions = stack.Where(x => IsKind(x, InteractionType.WiredEffect)
                                       && x.Type != WiredBoxType.AddonRandomEffect
                                       && (x is IWiredContextualAction action && action.IsNegative) == (negative == null && !passed)).ToArray();
        if (negative == null && !passed && actions.Length == 0) return false;
        foreach (var addon in addons.Where(x => x.AfterConditions))
            if (!InvokeRuntime(addon, context, () => addon.Apply(context))) return false;
        if (context.Policy.Addons.ActionPicker is { } stateful)
        {
            var ids = stateful.Pick(actions.Select(x => x.Item.Id).ToArray()).ToHashSet();
            actions = actions.Where(x => ids.Contains(x.Item.Id)).ToArray();
        }
        else if (context.Policy.ChooseActions is { } picker)
            actions = picker(actions).Where(actions.Contains).Distinct().ToArray();
        else if (stack.Any(x => x.Type == WiredBoxType.AddonRandomEffect) && actions.Length > 0)
            actions = [actions[Random.Shared.Next(actions.Length)]];
        Flash(source);
        var orderedDelay = Math.Max(0, context.Policy.DelayMilliseconds);
        var actor = actions.Any(x => x is IWiredFiringPreparation)
            ? context.Targets.ResolveUsers(context, context.Event.Actor == null ? [] : [context.Event.Actor.VirtualId], WiredSources.Selected).FirstOrDefault() : null;
        object[] preparationArguments = actor?.GetClient()?.GetHabbo() is { } habbo ? [habbo] : [];
        var legacy = new WiredExecutionContext(preparationArguments, context.Depth, _actorVisit?.Invoke(preparationArguments), context);
        var accepted = ScheduleActions(source, stack, actions, legacy, action =>
        {
            var delay = action is IWiredConfiguredItem configured ? Math.Max(0L, context.ConfigurationOf(configured).Delay) * 500 : GetDelay(action);
            if (context.Policy.OrderedEffects || context.Policy.Addons.ExecuteInOrder) return orderedDelay += delay;
            return Math.Max(0, context.Policy.DelayMilliseconds) + delay;
        });
        UpdateFastWork();
        return accepted;
    }

    private static bool MatchConditions(WiredExecutionPolicy policy, int matched, int total) => total == 0 || policy.ConditionMode switch
    {
        WiredConditionMode.Any => matched > 0, WiredConditionMode.None => true,
        WiredConditionMode.NoneMatch => matched == 0, WiredConditionMode.NotAll => matched < total,
        WiredConditionMode.LessThan => matched < policy.ConditionThreshold,
        WiredConditionMode.Exactly => matched == policy.ConditionThreshold,
        WiredConditionMode.MoreThan => matched > policy.ConditionThreshold, _ => matched == total
    };

    private void ComposeSelector(WiredRuntimeContext context, WiredSelectorResult result)
    {
        void Apply<T>(HashSet<T> pool, HashSet<T> triggering, HashSet<T> selected, IEnumerable<T> universe, WiredSelectionKind kind)
        {
            if (!result.Kind.HasFlag(kind)) return;
            var kept = result.Invert ? universe.Except(selected).ToHashSet() : selected;
            if (result.FiltersExisting)
            {
                var basis = context.SelectorKinds.HasFlag(kind) ? pool.ToArray() : triggering.ToArray();
                pool.Clear();
                pool.UnionWith(basis.Intersect(kept));
            }
            else pool.UnionWith(kept);
            context.SelectorKinds |= kind;
        }
        Apply(context.SelectorPool.FurniIds, context.Triggering.FurniIds, result.Selection.FurniIds,
            context.FurniIdentity.Keys, WiredSelectionKind.Furni);
        Apply(context.SelectorPool.UserIds, context.Triggering.UserIds, result.Selection.UserIds,
            context.UserIdentity.Keys, WiredSelectionKind.Users);
    }

    private bool InvokeRuntime(IWiredItem box, WiredRuntimeContext context, Func<bool> invoke)
    {
        if (_remaining <= 0 || !IsAttached(box)) return false;
        _remaining--;
        var previous = _runtimeContext;
        _runtimeContext = context;
        context.NowMilliseconds = _now();
        try { return invoke(); }
        catch (Exception error) { _error(error); return false; }
        finally { _runtimeContext = previous; }
    }

    private bool ExecuteRuntimeBody(IWiredItem box, WiredRuntimeContext context)
    {
        if (box is IWiredContextualItem contextual) return RuntimeSupported(contextual) && contextual.Execute(context);
        var actor = context.Event.Actor;
        if (actor == null) return box.Execute([]);
        if (!context.Targets.ResolveUsers(context, [actor.VirtualId], WiredSources.Selected).Contains(actor)) return false;
        return box.Execute(actor.GetClient()?.GetHabbo());
    }

    private void RunRuntimeTimersAndSignals()
    {
        var now = _now();
        if (_lastTimerPoll < 0 || now - _lastTimerPoll >= 50)
        {
            _lastTimerPoll = now;
            WiredRuntimeContext? snapshot = null;
            foreach (var timer in _items.Values.OfType<IWiredTimedTrigger>().Where(RuntimeSupported).ToArray())
            {
                if (_remaining <= 0) break;
                _remaining--;
                try
                {
                    if (timer.Poll(now) is not { } @event) continue;
                    snapshot ??= CreateContext(@event, 0);
                    var context = snapshot.Fork(@event, 0);
                    context.Trigger = timer;
                    SeedEvent(context);
                    if (RunRuntimeStack(timer, context, null)) Flash(timer);
                }
                catch (Exception error) { _error(error); }
            }
        }
        while (_remaining > 0 && _signals.TryDequeue(out var signal))
        {
            _remaining--;
            var receiver = _targets!.AllFurni().FirstOrDefault(x => x.Id == signal.Receiver.Id);
            if (!ReferenceEquals(receiver, signal.Receiver) || receiver.MovementGeneration != signal.Generation) continue;
            foreach (var trigger in _items.Values.OfType<IWiredContextualTrigger>()
                         .Where(x => RuntimeSupported(x) && x.Events.Contains(WiredEventKind.Signal)
                             && x.Item.GetX == receiver.GetX && x.Item.GetY == receiver.GetY).ToArray())
            {
                var context = signal.Context.Fork(signal.Context.Event, signal.Context.Depth);
                context.Signal = new(signal.Context.Signal!.Selection, signal.Context.Signal.Values);
                context.Triggering = signal.Context.Triggering.Copy();
                context.Selected = context.Triggering.Copy();
                context.Trigger = trigger;
                context.Capture(GetStack(trigger));
                if (InvokeRuntime(trigger, context, () => trigger.Execute(context))) RunRuntimeStack(trigger, context, signal.Negative);
            }
        }
    }

    private static bool RuntimeSupported(IWiredConfiguredItem box) => box.Descriptor.Support == WiredBoxSupport.Implemented;

    private void ResetRuntimeTile(int x, int y)
    {
        foreach (var addon in _items.Values.OfType<IWiredContextualAddon>().Where(a => a.Item.GetX == x && a.Item.GetY == y)) addon.Reset();
        foreach (var timer in _items.Values.OfType<IWiredTimedTrigger>().Where(a => a.Item.GetX == x && a.Item.GetY == y)) timer.Reset(_now());
    }

    private void UpdateRuntimeItems()
    {
        foreach (var box in _items.Values)
        {
            var position = (box.Item.GetX, box.Item.GetY, box.Item.GetZ);
            if (_runtimePositions.TryGetValue(box.Item.Id, out var previous) && previous != position)
            {
                ResetRuntimeTile(previous.X, previous.Y);
                ResetRuntimeTile(position.GetX, position.GetY);
            }
            _runtimePositions[box.Item.Id] = position;
        }
        foreach (var id in _runtimePositions.Keys.Except(_items.Keys).ToArray()) _runtimePositions.Remove(id);
        UpdateFastWork();
    }

    private void UpdateFastWork()
    {
        var required = _runtimeRoom != null && (_signals.Count > 0 || _items.Values.OfType<IWiredTimedTrigger>().Any(RuntimeSupported)
            || _pending.Any(chain => chain.Context.Runtime != null));
        var next = required ? 1 : 0;
        if (Interlocked.Exchange(ref _fastWork, next) != next) _fastWorkObserver?.Invoke(required);
    }

    private sealed record PendingSignal(Item Receiver, long Generation, WiredRuntimeContext Context, bool Negative);
}
