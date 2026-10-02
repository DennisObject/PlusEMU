namespace Plus.HabboHotel.Items.Wired;

// Room-owned dispatch, evaluation and scheduling. The lock also serializes legacy synchronous
// callbacks: context is scoped to a call, including events raised recursively by an action.
internal sealed class WiredStackEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<uint, IWiredItem> _items = new();
    private readonly Dictionary<(int X, int Y), IWiredItem[]> _stacks = new();
    private readonly PriorityQueue<ActionChain, (long Due, long Order)> _schedule = new();
    private readonly HashSet<ActionChain> _pending = new();
    private readonly Func<long> _now;
    private readonly Func<IWiredItem, bool> _attached;
    private readonly Func<object[], bool> _actorPresent;
    private readonly Action<Item> _flash;
    private readonly Action<Exception> _error;
    private readonly WiredEngineLimits _limits;
    private WiredExecutionContext? _context;
    private int _passDepth;
    private int _remaining;
    private long _sequence;

    public WiredStackEngine(Func<long> now, Func<IWiredItem, bool> attached,
        Func<object[], bool> actorPresent, Action<Item> flash, Action<Exception> error,
        WiredEngineLimits? limits = null)
    {
        _now = now;
        _attached = attached;
        _actorPresent = actorPresent;
        _flash = flash;
        _error = error;
        _limits = limits ?? new();
    }

    public bool Add(IWiredItem box)
    {
        lock (_sync) return _items.TryAdd(box.Item.Id, box);
    }

    public bool Remove(uint id)
    {
        lock (_sync)
        {
            if (!_items.Remove(id, out var removed)) return false;
            CancelPending(removed);
            return true;
        }
    }

    public bool TryGet(uint id, out IWiredItem box)
    {
        lock (_sync) return _items.TryGetValue(id, out box!);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            _stacks.Clear();
            _schedule.Clear();
            _pending.Clear();
        }
    }

    // A successful save cancels firings that captured this stack. The legacy parser still
    // mutates outside this lock; atomic validation/application belongs to the save adapter.
    public void CancelPending(IWiredItem box)
    {
        lock (_sync)
        {
            _pending.RemoveWhere(chain => chain.Contains(box));
            var kept = _schedule.UnorderedItems.Where(x => _pending.Contains(x.Element)).ToArray();
            _schedule.Clear();
            foreach (var entry in kept) _schedule.Enqueue(entry.Element, entry.Priority);
        }
    }

    public ICollection<IWiredItem> GetBoxes(IWiredItem source, InteractionType kind) => Pass(() =>
        GetStack(source).Where(x => x.Item.Definition.InteractionType == kind).ToList());

    public bool Dispatch(WiredBoxType type, params object[] arguments) => Pass(() =>
    {
        RefreshStacks();
        var context = new WiredExecutionContext((arguments ?? []).ToArray(), (_context?.Depth ?? -1) + 1);
        if (context.Depth > _limits.MaxDepth) return false;
        var matched = false;
        // Each registered trigger is visited once, even when a tile has several of the same type.
        foreach (var trigger in _stacks.Values.SelectMany(x => x)
                     .Where(x => x.Type == type && IsKind(x, InteractionType.WiredTrigger))
                     .OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).ToArray())
        {
            if (_remaining <= 0) break;
            matched |= Execute(trigger, context);
        }
        return matched;
    });

    public bool RunStack(IWiredItem source, object[] arguments, Action? onAccepted = null) => Pass(() =>
        RunStackCore(source, new(arguments.ToArray(), _context?.Depth ?? 0), null, onAccepted));

    // Legacy repeaters gate each condition on any room actor independently, then run actorless
    // room actions once. This quantifier is intentional and covered separately from actor events.
    public bool RunPeriodicStack(IWiredItem source, object[][] actors) => Pass(() =>
        RunStackCore(source, new([], 0), actors, null));

    public bool CallStacks(IEnumerable<Item> targets, object[] arguments) => Pass(() =>
    {
        var depth = (_context?.Depth ?? 0) + 1;
        if (depth > _limits.MaxDepth) return false;
        var matched = false;
        var visited = new HashSet<(int, int)>();
        foreach (var target in targets.OrderBy(x => x.GetZ).ThenBy(x => x.Id).ToArray())
        {
            if (!_items.TryGetValue(target.Id, out var box) || !IsAttached(box)) continue;
            if (!visited.Add((target.GetX, target.GetY))) continue;
            matched |= RunStackCore(box, new(arguments.ToArray(), depth), null, null);
        }
        return matched;
    });

    public void OnCycle() => Pass(() =>
    {
        RefreshStacks();
        foreach (var box in _items.Values.ToArray())
        {
            // IWiredCycle remains the saved-delay contract; only periodic triggers tick.
            if (!IsAttached(box) || !IsKind(box, InteractionType.WiredTrigger) || box is not IWiredCycle cycle)
                continue;
            if (_remaining <= 0) break;
            if (cycle.TickCount > 0) cycle.TickCount--;
            else
            {
                _remaining--;
                try { cycle.OnCycle(); }
                catch (Exception e) { _error(e); }
            }
        }
        var now = _now();
        while (_remaining > 0 && _schedule.TryPeek(out _, out var priority) && priority.Due <= now)
        {
            var chain = _schedule.Dequeue();
            ContinueChain(chain, now);
        }
        return true;
    });

    private bool RunStackCore(IWiredItem source, WiredExecutionContext context,
        object[][]? conditionActors, Action? onAccepted)
    {
        var firedAt = _now();
        if (context.Depth > _limits.MaxDepth || !_actorPresent(context.Arguments)) return false;
        var stack = GetStack(source);
        if (stack.Length == 0) return false;
        var conditions = stack.Where(x => IsKind(x, InteractionType.WiredCondition)).ToArray();
        foreach (var condition in conditions)
        {
            if (_remaining <= 0) return false;
            var passed = conditionActors == null
                ? Execute(condition, context)
                : conditionActors.Any(actor => Execute(condition, new(actor, context.Depth)));
            if (!passed) return false;
            Flash(condition);
        }
        var addons = stack.Where(x => x.Type == WiredBoxType.AddonRandomEffect).ToArray();
        var actions = stack.Where(x => IsKind(x, InteractionType.WiredEffect)
                                      && x.Type != WiredBoxType.AddonRandomEffect).ToArray();
        if (addons.Length > 0)
        {
            foreach (var addon in addons) Flash(addon);
            actions = actions.Length == 0 ? [] : [actions[Random.Shared.Next(actions.Length)]];
        }
        if (actions.Length > 0 && _pending.Count >= _limits.MaxPendingStacks) return false;
        onAccepted?.Invoke();
        Flash(source);
        if (actions.Length > 0)
        {
            var scheduled = actions.Select(action => new ScheduledAction(action, firedAt + GetDelay(action)))
                .OrderBy(action => action.Due).ThenBy(action => action.Box.Item.GetZ)
                .ThenBy(action => action.Box.Item.Id).ToArray();
            var chain = new ActionChain(source, stack, scheduled, context);
            _pending.Add(chain);
            ContinueChain(chain, _now());
        }
        // This is synchronous stack acceptance (including chat consumption), not action success.
        return true;
    }

    private void ContinueChain(ActionChain chain, long now)
    {
        while (chain.Next < chain.Actions.Length)
        {
            if (!_pending.Contains(chain)) return;
            if (!IsAttached(chain.Source) || (chain.Source.Item.GetX, chain.Source.Item.GetY) != chain.Tile
                || !_actorPresent(chain.Context.Arguments))
            {
                _pending.Remove(chain);
                return;
            }
            var scheduled = chain.Actions[chain.Next];
            var action = scheduled.Box;
            if (!IsAttached(action) || (action.Item.GetX, action.Item.GetY) != chain.Tile)
            {
                chain.Next++;
                continue;
            }
            if (_remaining <= 0)
            {
                Enqueue(chain, Math.Max(scheduled.Due, now));
                return;
            }
            if (scheduled.Due > now)
            {
                Enqueue(chain, scheduled.Due);
                return;
            }
            Execute(action, chain.Context);
            Flash(action);
            chain.Next++;
            // Preserve Octane/Plus independent delays: every action's deadline belongs to the
            // firing, so an earlier delayed action cannot postpone an otherwise immediate one.
            now = _now();
        }
        _pending.Remove(chain);
    }

    private void Enqueue(ActionChain chain, long due) => _schedule.Enqueue(chain, (due, ++_sequence));

    private static long GetDelay(IWiredItem action) => action is IWiredActionDelay intrinsic
        ? Math.Max(0, intrinsic.DelayMilliseconds)
        : action is IWiredCycle cycle ? Math.Max(0L, cycle.Delay) * 500 : 0;

    private void Flash(IWiredItem box)
    {
        try { _flash(box.Item); }
        catch (Exception e) { _error(e); }
    }

    private bool Execute(IWiredItem box, WiredExecutionContext context)
    {
        if (_remaining <= 0 || !IsAttached(box) || !_actorPresent(context.Arguments)) return false;
        _remaining--;
        var previous = _context;
        _context = context;
        try { return box.Execute(context.Arguments); }
        catch (Exception e) { _error(e); return false; }
        finally { _context = previous; }
    }

    private bool IsAttached(IWiredItem box) => _items.TryGetValue(box.Item.Id, out var registered)
        && ReferenceEquals(registered, box) && _attached(box);

    private static bool IsKind(IWiredItem box, InteractionType kind) => box.Item.Definition.InteractionType == kind;

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
            if (!_attached(box)) Remove(box.Item.Id);
        _stacks.Clear();
        foreach (var group in _items.Values.GroupBy(x => (x.Item.GetX, x.Item.GetY)))
            _stacks[group.Key] = group.OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).ToArray();
    }

    private T Pass<T>(Func<T> body)
    {
        lock (_sync)
        {
            if (_passDepth++ == 0) _remaining = _limits.MaxExecutionsPerPass;
            try { return body(); }
            finally { _passDepth--; }
        }
    }

    private sealed record ScheduledAction(IWiredItem Box, long Due);

    private sealed class ActionChain(IWiredItem source, IWiredItem[] stack,
        ScheduledAction[] actions, WiredExecutionContext context)
    {
        public IWiredItem Source { get; } = source;
        public (int, int) Tile { get; } = (source.Item.GetX, source.Item.GetY);
        public ScheduledAction[] Actions { get; } = actions;
        public WiredExecutionContext Context { get; } = context;
        public int Next { get; set; }
        public bool Contains(IWiredItem box) => stack.Contains(box);
    }
}
