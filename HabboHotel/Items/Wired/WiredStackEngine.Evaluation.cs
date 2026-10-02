using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired;

internal sealed partial class WiredStackEngine
{
    private RuntimeFiring? _activeFiring;
    private PendingDispatch? _transferringDispatch;
    private int _queuedSlots;
    private int PendingCount => _pending.Count + _queuedSlots - (_transferringDispatch != null
        && _dispatches.TryPeek(out var head) && ReferenceEquals(head, _transferringDispatch) ? 1 : 0);

    private void SnapshotDispatch(PendingDispatch pending)
    {
        if (pending.Triggers != null) return;
        pending.Triggers = _items.Values.OfType<IWiredContextualTrigger>()
            .Where(trigger => RuntimeSupported(trigger) && trigger.Events.Contains(pending.Event.Kind)
                && (pending.Signal == null || trigger.Item.GetX == pending.Signal.Receiver.GetX && trigger.Item.GetY == pending.Signal.Receiver.GetY))
            .OrderBy(trigger => trigger.Item.GetZ).ThenBy(trigger => trigger.Item.Id).ToArray();
        var legacyType = pending.Event.Kind switch
        {
            WiredEventKind.GameStart => WiredBoxType.TriggerGameStarts,
            WiredEventKind.GameEnd => WiredBoxType.TriggerGameEnds,
            _ => WiredBoxType.None
        };
        if (pending.IncludeLegacy && legacyType != WiredBoxType.None)
            pending.LegacyTriggers = _items.Values.Where(box => box is not IWiredContextualTrigger && box.Type == legacyType)
                .OrderBy(box => box.Item.GetZ).ThenBy(box => box.Item.Id).ToArray();
        pending.TriggerPositions = pending.Triggers.ToDictionary(trigger => trigger,
            trigger => (trigger.Item.GetX, trigger.Item.GetY, trigger.Item.GetZ, trigger.Item.MovementGeneration));
        pending.Slots = Math.Max(1, pending.Triggers.Length + pending.LegacyTriggers.Length);
        pending.CapturedBoxes = pending.Triggers.SelectMany(trigger => GetStack(trigger)).Distinct().ToArray();
        pending.CapturedPositions = pending.CapturedBoxes.Select(box => (box, box.Item.GetX, box.Item.GetY, box.Item.GetZ, box.Item.MovementGeneration)).ToArray();
    }

    private void QueueDispatch(PendingDispatch pending)
    { pending.IsQueued = true; _queuedSlots += pending.Slots; _dispatches.Enqueue(pending); }

    private void CompleteDispatchSlot(PendingDispatch pending)
    {
        if (pending.Slots <= 0) return;
        pending.Slots--;
        if (pending.IsQueued) _queuedSlots--;
    }

    private void RemoveDispatchHead()
    { var pending = _dispatches.Dequeue(); _queuedSlots -= pending.Slots; pending.IsQueued = false; pending.Current?.Dispose(); }

    private bool AdvanceDispatch(PendingDispatch pending)
    {
        var queued = pending.IsQueued;
        if (pending.CapturedPositions.Any(position => !IsAttached(position.Box)
            || (position.Box.Item.GetX, position.Box.Item.GetY, position.Box.Item.GetZ, position.Box.Item.MovementGeneration)
                != (position.X, position.Y, position.Z, position.Generation)))
        { pending.Current?.Dispose(); return true; }
        if (pending.Signal is { } signal)
        {
            var receiver = _targets!.AllFurni().FirstOrDefault(x => x.Id == signal.Receiver.Id);
            if (!ReferenceEquals(receiver, signal.Receiver) || receiver.MovementGeneration != signal.Generation)
            { pending.Current?.Dispose(); return true; }
        }
        SnapshotDispatch(pending);
        if (!pending.Initialized)
        {
            if (pending.Event.EventItem is { } item && !_targets!.AllFurni().Contains(item)) return true;
            pending.Root = pending.Signal?.Context ?? pending.Root ?? CreateContext(pending.Event, pending.Depth);
            if (!IsActorPresent(new([], pending.Depth, Runtime: pending.Root))) return true;
            ObserveEvent?.Invoke(pending.Event, _now());
            pending.Initialized = true;
        }
        while (true)
        {
            if (queued && !pending.IsQueued) { pending.Current?.Dispose(); return true; }
            if (pending.Current != null)
            {
                var transfer = _dispatches.TryPeek(out var head) && ReferenceEquals(head, pending);
                if (!ResumeFiring(pending.Current, transfer)) return false;
                pending.Accepted |= pending.Current.Accepted;
                if (pending.Event.Kind == WiredEventKind.ClickUser && pending.Current.Accepted && pending.Current.ConditionsPassed
                    && pending.Current.Context.Trigger is IWiredClickTrigger clickTrigger)
                {
                    var settings = clickTrigger.ClickSettings(pending.Current.Context);
                    pending.Click = new(true, pending.Click.BlockMenu || settings.BlockMenu, pending.Click.DoNotRotate || settings.DoNotRotate);
                }
                if (pending.Event.Kind == WiredEventKind.Speech && pending.Current.Accepted
                    && pending.Current.Context.Trigger is IWiredContextualTrigger speechTrigger)
                    pending.ConsumedChat |= speechTrigger.HidesChat(pending.Current.Context);
                pending.Current.Dispose(); pending.Current = null;
                CompleteDispatchSlot(pending);
            }
            if (pending.LegacyNext < pending.LegacyTriggers.Length)
            {
                if (_remaining <= 0) return false;
                var box = pending.LegacyTriggers[pending.LegacyNext++];
                var previous = _legacyRuntimeEvent;
                _legacyRuntimeEvent = pending.Event;
                try { Execute(box, new([], pending.Depth)); }
                finally { _legacyRuntimeEvent = previous; CompleteDispatchSlot(pending); }
                continue;
            }
            if (pending.Next >= pending.Triggers!.Length) return true;
            if (_remaining <= 0) return false;
            var trigger = pending.Triggers[pending.Next++];
            if (!IsAttached(trigger) || pending.TriggerPositions[trigger]
                != (trigger.Item.GetX, trigger.Item.GetY, trigger.Item.GetZ, trigger.Item.MovementGeneration))
            { CompleteDispatchSlot(pending); continue; }
            var context = pending.Root!.Fork(pending.Event, pending.Depth);
            context.Trigger = trigger;
            if (pending.Signal is { } forwarded)
            {
                context.Signal = new(forwarded.Context.Signal!.Selection, forwarded.Context.Signal.Values);
                context.Triggering = forwarded.Context.Triggering.Copy(); context.Selected = context.Triggering.Copy();
            }
            else SeedEvent(context);
            context.Capture(GetStack(trigger));
            if (InvokeRuntime(trigger, context, () => trigger.Execute(context)))
                pending.Current = BeginFiring(trigger, context, pending.Signal?.Negative);
            else CompleteDispatchSlot(pending);
        }
    }

    private RuntimeFiring BeginFiring(IWiredItem source, WiredRuntimeContext context, bool? negative, object[][]? actors = null)
    {
        var firing = new RuntimeFiring(source, GetStack(source), context, _now());
        context.Capture(firing.Stack);
        firing.Steps = EvaluateFiring(firing, negative, actors).GetEnumerator();
        return firing;
    }

    private bool ResumeFiring(RuntimeFiring firing, bool transfer = false)
    {
        if (firing.Cancelled || !IsActorPresent(new([], firing.Context.Depth, Runtime: firing.Context))
            || firing.Positions.Any(position => !IsAttached(position.Box) || position.Box.Item.MovementGeneration != position.Generation
                || (position.Box.Item.GetX, position.Box.Item.GetY, position.Box.Item.GetZ) != (position.X, position.Y, position.Z)))
        { firing.Cancelled = true; return true; }
        var previous = _activeFiring;
        _activeFiring = firing;
        try
        {
            while (true)
            {
                if (firing.Next == null)
                {
                    if (!firing.Steps.MoveNext()) return true;
                    firing.Next = firing.Steps.Current;
                }
                if (firing.Next.CostsExecution && _remaining <= 0) return false;
                var step = firing.Next;
                firing.Next = null;
                var previousTransfer = _transferringDispatch;
                _transferringDispatch = transfer && step.TransfersReservation ? _dispatches.Peek() : null;
                try { step.Run(); }
                finally { _transferringDispatch = previousTransfer; }
                if (firing.Cancelled) return true;
            }
        }
        catch (Exception error) { _error(error); firing.Cancelled = true; return true; }
        finally { _activeFiring = previous; }
    }

    private IEnumerable<EvaluationStep> EvaluateFiring(RuntimeFiring firing, bool? negative, object[][]? actors)
    {
        var context = firing.Context;
        var stack = firing.Stack;
        foreach (var selector in stack.OfType<IWiredContextualSelector>().Where(RuntimeSupported))
        {
            var passed = false;
            yield return new(() => passed = InvokeRuntime(selector, context, () =>
            { ComposeSelector(context, selector.Select(context)); return true; }));
            if (!passed) yield break;
        }
        context.Selected = new(
            context.SelectorKinds.HasFlag(WiredSelectionKind.Furni) ? context.SelectorPool.FurniIds : context.Triggering.FurniIds,
            context.SelectorKinds.HasFlag(WiredSelectionKind.Users) ? context.SelectorPool.UserIds : context.Triggering.UserIds);
        var addons = stack.OfType<IWiredContextualAddon>().Where(RuntimeSupported).ToArray();
        foreach (var addon in addons.Where(x => !x.AfterConditions)
                     .OrderBy(x => x.Descriptor.CanonicalName is "wf_xtra_filter_furni" or "wf_xtra_filter_users" ? 0 : 1))
        {
            var passed = false;
            yield return new(() => passed = InvokeRuntime(addon, context, () => addon.Apply(context)));
            if (!passed) yield break;
        }
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
            var passed = false;
            foreach (var step in EvaluateCondition(condition, context, actors, result => passed = result)) yield return step;
            if (passed) matched++;
        }
        var conditionsPassed = MatchConditions(context.Policy, matched, ordinary.Length);
        if (scoped != null)
        {
            matched = 0;
            foreach (var condition in grouped)
            {
                var passed = false;
                foreach (var step in EvaluateCondition(condition, context, actors, result => passed = result)) yield return step;
                if (passed) matched++;
            }
            conditionsPassed &= WiredConditionPolicyEvaluator.Matches(scoped.Mode, matched, grouped.Length, scoped.Count);
        }
        firing.ConditionsPassed = conditionsPassed;
        if (negative is { } requested && (requested ? conditionsPassed : !conditionsPassed)) yield break;
        var actions = stack.Where(x => IsKind(x, InteractionType.WiredEffect) && x.Type != WiredBoxType.AddonRandomEffect
            && (x is IWiredContextualAction action && action.IsNegative) == (negative == null && !conditionsPassed)).ToArray();
        if (negative == null && !conditionsPassed && actions.Length == 0) yield break;
        foreach (var addon in addons.Where(x => x.AfterConditions))
        {
            var passed = false;
            yield return new(() => passed = InvokeRuntime(addon, context, () => addon.Apply(context)));
            if (!passed) yield break;
        }
        if (context.Policy.Addons.ActionPicker is { } stateful)
        {
            var ids = stateful.Pick(actions.Select(x => x.Item.Id).ToArray()).ToHashSet();
            actions = actions.Where(x => ids.Contains(x.Item.Id)).ToArray();
        }
        else if (context.Policy.ChooseActions is { } picker)
            actions = picker(actions).Where(actions.Contains).Distinct().ToArray();
        else if (stack.Any(x => x.Type == WiredBoxType.AddonRandomEffect) && actions.Length > 0)
            actions = [actions[Random.Shared.Next(actions.Length)]];
        var actor = actions.Any(x => x is IWiredFiringPreparation)
            ? context.Targets.ResolveUsers(context, context.Event.Actor == null ? [] : [context.Event.Actor.VirtualId], WiredSources.Selected).FirstOrDefault() : null;
        object[] arguments = actor?.GetClient()?.GetHabbo() is { } habbo ? [habbo] : [];
        var legacy = new WiredExecutionContext(arguments, context.Depth, _actorVisit?.Invoke(arguments), context);
        var ready = new List<IWiredItem>();
        foreach (var action in actions)
        {
            if (action is IWiredFiringPreparation preparation)
            {
                var passed = false;
                yield return new(() => passed = Invoke(action, legacy, () => preparation.Prepare(arguments)));
                if (!passed) continue;
            }
            ready.Add(action);
        }
        yield return new(() =>
        {
            var ordered = Math.Max(0, context.Policy.DelayMilliseconds);
            firing.Accepted = ScheduleActions(firing.Source, stack, ready.ToArray(), legacy, action =>
            {
                var delay = action is IWiredConfiguredItem configured ? context.ConfigurationOf(configured).Delay * 500L : GetDelay(action);
                return context.Policy.OrderedEffects || context.Policy.Addons.ExecuteInOrder
                    ? ordered += delay : Math.Max(0, context.Policy.DelayMilliseconds) + delay;
            }, firing.FiredAt, prepared: true);
            if (firing.Accepted) Flash(firing.Source);
        }, CostsExecution: false, TransfersReservation: true);
    }

    private IEnumerable<EvaluationStep> EvaluateCondition(IWiredItem condition, WiredRuntimeContext context,
        object[][]? actors, Action<bool> result)
    {
        if (actors == null)
        { yield return new(() => result(InvokeRuntime(condition, context, () => ExecuteRuntimeBody(condition, context)))); yield break; }
        foreach (var arguments in actors)
        {
            var passed = false;
            if (condition is not IWiredContextualItem)
                yield return new(() => passed = Execute(condition, CreateContext(arguments, context.Depth)));
            else
            {
                var actor = arguments.OfType<RoomUser>().FirstOrDefault() ?? _actorVisit?.Invoke(arguments) as RoomUser;
                if (actor == null || !context.UserIdentity.TryGetValue(actor.VirtualId, out var original) || !ReferenceEquals(actor, original)
                    || !context.Targets.ResolveUsers(context, [actor.VirtualId], WiredSources.Selected, raw: true).Contains(actor)) continue;
                var actorContext = context.ForActor(actor);
                yield return new(() => passed = InvokeRuntime(condition, actorContext, () => ExecuteRuntimeBody(condition, actorContext)));
            }
            if (passed) { result(true); yield break; }
        }
        result(false);
    }

    private sealed record EvaluationStep(Action Run, bool CostsExecution = true, bool TransfersReservation = false);
    private sealed class RuntimeFiring(IWiredItem source, IWiredItem[] stack, WiredRuntimeContext context, long firedAt) : IDisposable
    {
        public IWiredItem Source { get; } = source;
        public IWiredItem[] Stack { get; } = stack;
        public WiredRuntimeContext Context { get; } = context;
        public long FiredAt { get; } = firedAt;
        public (IWiredItem Box, long Generation, int X, int Y, double Z)[] Positions { get; } = stack.Select(box => (box, box.Item.MovementGeneration, box.Item.GetX, box.Item.GetY, box.Item.GetZ)).ToArray();
        public IEnumerator<EvaluationStep> Steps { get; set; } = null!;
        public EvaluationStep? Next { get; set; }
        public bool Accepted, Cancelled, ConditionsPassed;
        public void Dispose() => Steps.Dispose();
    }
}
