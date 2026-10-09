using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
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
    public Func<WiredRuntimeContext, IWiredContextualTrigger, bool?>? CaptureSpeech { get; set; }
    public Action<WiredRuntimeEvent>? SpeechHidden { get; set; }
    private bool _legacySpeechAdmission;
    private IWiredItem? _legacySpeechOrigin;
    private readonly Queue<PendingDispatch> _dispatches = new();
    private Func<bool>? _externalFastWork;
    private Action<long>? _pollExternal;
    private Action? _flushExternal;
    private int? _queuedDepth;
    public Action<IWiredConfiguredItem>? ConfigurationPublished { get; set; }
    private readonly Dictionary<uint, (int X, int Y, double Z)> _runtimePositions = [];
    private int _fastWork;
    private Action<bool>? _fastWorkObserver;
    private int _timerCursor;

    public void BindRuntime(Room room, WiredTargetResolver targets, IWiredRuntimeOperations operations,
        Func<bool>? needsFast = null, Action<long>? poll = null, Action? flush = null)
    {
        lock (_sync) {
            _runtimeRoom = room;
            _targets = targets;
            _operations = operations;
            _externalFastWork = needsFast;
            _pollExternal = poll;
            _flushExternal = flush;
        }
    }

    public long NowMilliseconds => _now();

    public bool Mutate(Func<bool> mutation) => Pass(() =>
    {
        SealPublication();

        try {
            return mutation();
        }
        finally {
            UpdateFastWork();
        }
    });

    public bool Enqueue(WiredRuntimeEvent @event, int? depth = null) => Pass(() => EnqueueCore(@event, depth));

    private bool EnqueueCore(WiredRuntimeEvent @event, int? depth = null, HeadingCollisionProof? collision = null, bool sealOnRejection = false)
    {
        var eventDepth = depth ?? (_runtimeContext?.Depth ?? -1) + 1;

        if (sealOnRejection && eventDepth > _limits.MaxDepth) {
            SealPublication();
        }

        if (TooDeep(eventDepth) || _runtimeRoom == null) {
            return false;
        }

        RefreshStacks();
        var dispatch = new PendingDispatch(@event, eventDepth) { CollisionProof = collision };
        SnapshotDispatch(dispatch);

        if (sealOnRejection && PendingCount + dispatch.Slots > _limits.MaxPendingStacks) {
            SealPublication();
        }

        if (QueueFull(dispatch.Slots)) {
            return false;
        }

        QueueDispatch(dispatch);
        UpdateFastWork();

        return true;
    }

    public void ActorLeaving(RoomUser actor) => Pass(() =>
    {
        if (_activeFiring?.Context.Event.Kind != WiredEventKind.Leave && ReferenceEquals(_activeFiring?.Context.Event.Actor, actor)) {
            _activeFiring!.Cancelled = true;
        }

        _pending.RemoveWhere(chain => chain.Context.Runtime?.Event.Kind != WiredEventKind.Leave
            && (ReferenceEquals(chain.Context.Runtime?.Event.Actor, actor) || ReferenceEquals(chain.Context.ActorVisit, actor)));
        PruneSchedule(); // Cancellation runs while the original visit can still restore transient effects.
        var kept = _dispatches.Where(pending => pending.Event.Kind == WiredEventKind.Leave
            || !ReferenceEquals(pending.Event.Actor, actor)).ToArray();

        foreach (var pending in _dispatches.Except(kept)) {
            pending.Current?.Dispose();
        }

        foreach (var pending in _dispatches) {
            pending.IsQueued = false;
        }

        _dispatches.Clear();
        _queuedSlots = 0;

        foreach (var pending in kept) {
            QueueDispatch(pending);
        }

        UpdateFastWork();

        return true;
    });

    public bool NeedsFastCycle => Volatile.Read(ref _fastWork) != 0;

    public void ObserveFastWork(Action<bool>? observer)
    {
        lock (_sync) {
            _fastWorkObserver = observer;
            observer?.Invoke(NeedsFastCycle);
        }
    }

    // Compatibility seam for synchronous speech decisions and explicit engine evaluations.
    // Production events otherwise enter through Enqueue and evaluate on the next room boundary.
    public bool DispatchSynchronously(WiredRuntimeEvent @event) => Pass(() => DispatchCore(@event).Accepted);

    public WiredClickResult DispatchClickUser(RoomUser actor, RoomUser target) => Pass(() =>
    {
        if (_targets == null || !_targets.AllUsers().Contains(actor) || !_targets.AllUsers().Contains(target)) {
            return default;
        }

        return DispatchCore(new(WiredEventKind.ClickUser) { Actor = actor, TargetUser = target }).Click;
    });

    private (bool Accepted, WiredClickResult Click) DispatchCore(WiredRuntimeEvent @event)
    {
        if (_runtimeRoom == null) {
            return default;
        }

        var depth = _queuedDepth ?? (_runtimeContext?.Depth ?? _context?.Depth ?? -1) + 1;
        _queuedDepth = null;

        if (TooDeep(depth)) {
            return default;
        }

        RefreshStacks();
        var dispatch = new PendingDispatch(@event, depth) { IncludeLegacy = false, ResumeImmediately = @event.Kind == WiredEventKind.Speech };
        var complete = AdvanceDispatch(dispatch);

        if (!complete) {
            // A synchronous speech decision cannot consume chat on an unfinished condition gate.
            if (@event.Kind is not (WiredEventKind.Speech or WiredEventKind.ClickUser) && !QueueFull(dispatch.Slots)) {
                QueueDispatch(dispatch);
                dispatch.Accepted = true;
            }
            else {
                dispatch.Current?.Dispose();
            }
        }

        DrainReadySignals();
        UpdateFastWork();

        if (dispatch.ConsumedChat) {
            SpeechHidden?.Invoke(@event);
        }

        return (@event.Kind == WiredEventKind.Speech ? dispatch.ConsumedChat : dispatch.Accepted, dispatch.Click);
    }

    public bool DispatchLegacy(WiredBoxType type, WiredRuntimeEvent? typed, object[] arguments) => Pass(() =>
    {
        var previous = _legacyRuntimeEvent;
        var previousSpeechAdmission = _legacySpeechAdmission;
        _legacySpeechAdmission = typed?.Kind == WiredEventKind.Speech;
        var queuedDepth = _queuedDepth;
        _legacyRuntimeEvent = typed;

        try {
            var accepted = Dispatch(type, arguments);

            if (typed != null) {
                _queuedDepth = queuedDepth;
                accepted |= typed.Kind == WiredEventKind.Speech ? DispatchSynchronously(typed) : Enqueue(typed, _queuedDepth);
            }

            return accepted;
        }
        finally {
            _legacyRuntimeEvent = previous;
            _legacySpeechAdmission = previousSpeechAdmission;
        }
    });

    public bool CallStacks(WiredRuntimeContext parent, IEnumerable<Item> targets, bool negative = false) => Pass(() =>
    {
        if (!ReferenceEquals(parent.Room, _runtimeRoom) || TooDeep(parent.Depth + 1)) {
            return false;
        }

        var accepted = false;
        var visited = new HashSet<(int, int)>();

        foreach (var item in targets.OrderBy(x => x.GetZ).ThenBy(x => x.Id).ToArray()) {
            if (OutOfBudget()) {
                break;
            }

            if (!parent.Targets.IsAttached(item) || !parent.FurniIdentity.TryGetValue(item.Id, out var captured)
                || !ReferenceEquals(captured, item)) {
                continue;
            }

            RefreshStacks();

            if (!_stacks.TryGetValue((item.GetX, item.GetY), out var tile)) {
                continue;
            }

            var source = tile.FirstOrDefault(IsAttached);

            if (source == null || !visited.Add((item.GetX, item.GetY))) {
                continue;
            }

            var dispatch = new PendingDispatch(parent.Event, parent.Depth + 1,
                call: new(source, parent, parent.Selected.Copy()))
            { ResumeImmediately = true, Publication = parent.Publication };
            SnapshotDispatch(dispatch);

            if (QueueFull(dispatch.Slots)) {
                continue;
            }

            // A call invokes its whole action stack, rather than publishing another external event.
            // Native speech controls establish same-pass zero effects; other origins use the same box rule.
            if (!AdvanceDispatch(dispatch)) {
                QueueDispatch(dispatch);
                accepted = true;
            }
            else {
                accepted |= dispatch.Accepted;
            }
        }

        UpdateFastWork();

        return accepted;
    });

    internal static bool IsSignalAntenna(Item item) => item.IsFloorItem
        && string.Equals(item.Definition.InteractionName, "antenna", StringComparison.OrdinalIgnoreCase);

    public bool SendSignal(WiredRuntimeContext parent, IEnumerable<Item> receivers,
        WiredSelection selection, bool negative = false) => Pass(() =>
    {
        if (!ReferenceEquals(parent.Room, _runtimeRoom) || TooDeep(parent.Depth + 1)) {
            return false;
        }

        var live = _targets!.AllFurni().ToDictionary(x => x.Id);
        var antennas = receivers.DistinctBy(x => x.Id).Where(receiver =>
            live.TryGetValue(receiver.Id, out var attached) && ReferenceEquals(receiver, attached)
            && IsSignalAntenna(receiver)).ToArray();

        if (antennas.Length == 0) {
            return false;
        }

        var accepted = false;

        foreach (var antenna in antennas) {
            // Official delivery fires once per addressed antenna matching a receiving stack's picks.
            var child = parent.Fork(new(WiredEventKind.Signal)
            {
                Code = unchecked((int)antenna.Id),
                Actor = parent.Event.Actor,
                EventItem = parent.Event.EventItem
            }, parent.Depth + 1);
            child.Signal = new(selection, parent.Values);
            // Routing antennas and forwarded targets are independent from the original event's targets.
            child.Triggering = parent.Triggering.Copy();
            child.Selected = child.Triggering.Copy();
            var dispatch = new PendingDispatch(child.Event, child.Depth,
                new([(antenna, antenna.MovementGeneration)], child, parent))
            { ResumeImmediately = true, Publication = parent.Publication };
            SnapshotDispatch(dispatch);

            if (QueueFull(dispatch.Slots)) {
                break;
            }

            QueueDispatch(dispatch);
            accepted = true;
        }

        UpdateFastWork();

        return accepted;
    });

    public bool ScheduleAux(WiredRuntimeContext context, int delayMilliseconds, Action callback, Action? onCancelled = null) => Pass(() =>
    {
        var active = _executingAction;

        if (active == null || !ReferenceEquals(active.Chain.Context.Runtime, context)
            || !CanSchedule([active.Box])) {
            return false;
        }

        var chain = new ActionChain(active.Chain.Source, active.Chain.Stack, active.Chain.Context, 1);
        _pending.Add(chain);
        _schedule.Enqueue(new(chain, active.Box, callback, onCancelled),
            (_now() + Math.Max(0, delayMilliseconds), active.Box.Item.GetZ, active.Box.Item.Id, ++_sequence, 0));
        UpdateFastWork();

        return true;
    });

    public void ResetTimers(IEnumerable<Item> targets) => Pass(() =>
    {
        // One instant for the whole reset, and each stack once however many of its boxes are targeted.
        var now = _now();
        var tiles = new HashSet<(int X, int Y)>();

        foreach (var target in targets) {
            if (!_items.TryGetValue(target.Id, out var source) || !ReferenceEquals(source.Item, target)
                || !IsAttached(source) || !tiles.Add((source.Item.GetX, source.Item.GetY))) {
                continue;
            }

            // The room timer only: repeaters, legacy ones included, keep their period (Turbo RoomWiredSystem.ResetTimers).
            foreach (var box in GetStack(source)) {
                if (box is IWiredTimedTrigger timer) {
                    timer.ResetElapsed(now);
                }
            }
        }

        return true;
    });

    public bool PublishConfigured(IWiredConfiguredItem original, WiredConfiguration validated, Action persist) => Pass(() =>
    {
        if (!IsAttached(original) || !RuntimeSupported(original)) {
            return false;
        }

        persist();
        CancelPending(original);
        original.ApplyConfiguration(validated);
        ConfigurationPublished?.Invoke(original);
        ResetRuntimeTile(original.Item.GetX, original.Item.GetY);
        UpdateFastWork();

        return true;
    });

    public bool PublishPromotion(IWiredItem original, IWiredConfiguredItem candidate,
        WiredConfiguration validated, Action persist) => Pass(() =>
    {
        if (!IsAttached(original) || !ReferenceEquals(original.Item, candidate.Item) || !RuntimeSupported(candidate)) {
            return false;
        }

        persist();
        candidate.ApplyConfiguration(validated);
        CancelPending(original);
        _items[original.Item.Id] = candidate;
        _stacksDirty = true;
        ConfigurationPublished?.Invoke(candidate);
        ResetRuntimeTile(original.Item.GetX, original.Item.GetY);
        RefreshStacks();

        return true;
    });

    public bool PublishLegacy(IWiredItem original, IWiredItem candidate, Action persist) => Pass(() =>
    {
        if (!IsAttached(original) || original.Type != candidate.Type || original.Item.Id != candidate.Item.Id) {
            return false;
        }

        persist();
        CancelPending(original);
        original.StringData = candidate.StringData;
        original.BoolData = candidate.BoolData;
        original.ItemsData = candidate.ItemsData;
        original.SetItems = new(candidate.SetItems);

        if (original is IWiredCycle cycle && candidate is IWiredCycle prepared) {
            cycle.Delay = prepared.Delay;
        }

        ResetRuntimeTile(original.Item.GetX, original.Item.GetY);

        return true;
    });

    public void OnFastCycle() => Pass(() =>
    {
        if (_runtimeRoom == null || !NeedsFastCycle) {
            return false;
        }

        _cycle++;
        RefreshStacks();
        DrainDueActions();
        RunRuntimeTimersAndSignals();
        DrainDueActions();
        UpdateFastWork();

        return true;
    });

    private WiredRuntimeContext CreateContext(WiredRuntimeEvent @event, int depth) =>
        new(_runtimeRoom!, @event, _targets!, _operations!) { Depth = depth, NowMilliseconds = _now() };

    private static void SeedEvent(WiredRuntimeContext context)
    {
        if (context.Event.EventItem != null && context.Event.Kind != WiredEventKind.Variable) {
            context.Triggering.FurniIds.Add(context.Event.EventItem.Id);
        }

        var user = context.Event.Kind switch
        {
            WiredEventKind.BotReachedUser => context.Event.TargetUser,
            WiredEventKind.Variable => null,
            WiredEventKind.BotReachedFurni => null,
            _ => context.Event.Actor
        };

        if (user != null) {
            context.Triggering.UserIds.Add(user.VirtualId);
        }

        context.Selected = context.Triggering.Copy();
    }

    private bool RunRuntimeStack(IWiredItem source, WiredRuntimeContext context, bool? negative, object[][]? conditionActors = null, bool defer = true)
    {
        if (TooDeep(context.Depth) || !IsAttached(source)) {
            return false;
        }

        var firing = BeginFiring(source, context, negative, conditionActors);

        if (ResumeFiring(firing)) {
            firing.Dispose();
            DrainReadySignals();
            UpdateFastWork();

            return firing.Accepted;
        }

        // Chat consumption requires a completed synchronous decision. Do not defer its gate.
        if (!defer || context.Event.Kind == WiredEventKind.Speech || QueueFull(1)) {
            firing.Dispose();

            return false;
        }

        QueueDispatch(new(context.Event, context.Depth) { Current = firing, Triggers = [], Initialized = true });
        UpdateFastWork();

        return true;
    }

    private static bool MatchConditions(WiredExecutionPolicy policy, int matched, int total) => total == 0 || policy.ConditionMode switch
    {
        WiredConditionMode.Any => matched > 0,
        WiredConditionMode.None => true,
        WiredConditionMode.NoneMatch => matched == 0,
        WiredConditionMode.NotAll => matched < total,
        WiredConditionMode.LessThan => matched < policy.ConditionThreshold,
        WiredConditionMode.Exactly => matched == policy.ConditionThreshold,
        WiredConditionMode.MoreThan => matched > policy.ConditionThreshold,
        _ => matched == total
    };

    private void ComposeSelector(WiredRuntimeContext context, WiredSelectorResult result)
    {
        void Apply<T>(HashSet<T> pool, List<T> order, HashSet<T> triggering, HashSet<T> selected, IEnumerable<T> universe, WiredSelectionKind kind)
        {
            if (!result.Kind.HasFlag(kind)) {
                return;
            }

            var kept = (result.Invert ? universe.Except(selected) : selected).ToArray();

            if (result.FiltersExisting) {
                var basis = context.SelectorKinds.HasFlag(kind) ? order.ToArray() : triggering.ToArray();
                var matches = kept.ToHashSet();
                order.Clear();
                order.AddRange(basis.Where(matches.Contains));
                pool.Clear();
                pool.UnionWith(order);
            }
            else {
                foreach (var id in kept) {
                    if (pool.Add(id)) {
                        order.Add(id);
                    }
                }
            }

            context.SelectorKinds |= kind;
        }
        Apply(context.SelectorPool.FurniIds, context.SelectorFurniOrder, context.Triggering.FurniIds, result.Selection.FurniIds,
            context.FurniIdentity.Where(pair => pair.Value.IsFloorItem).Select(pair => pair.Key), WiredSelectionKind.Furni);
        Apply(context.SelectorPool.UserIds, context.SelectorUserOrder, context.Triggering.UserIds, result.Selection.UserIds,
            context.UserIdentity.Keys, WiredSelectionKind.Users);
    }

    private bool InvokeRuntime(IWiredItem box, WiredRuntimeContext context, Func<bool> invoke)
    {
        if (PublicationEvaluationIsSilent?.Invoke(box) != true) {
            SealPublication();
        }

        if (OutOfBudget() || !IsAttached(box)) {
            return false;
        }

        _remaining--;
        _passPeakDepth = Math.Max(_passPeakDepth, context.Depth);
        var previous = _runtimeContext;
        _runtimeContext = context;
        context.NowMilliseconds = _now();

        try {
            return invoke();
        }
        catch (Exception error) {
            SealPublication();
            _error(error);

            return false;
        }
        finally {
            _runtimeContext = previous;
        }
    }

    private bool ExecuteRuntimeBody(IWiredItem box, WiredRuntimeContext context)
    {
        if (box is IWiredContextualItem contextual) {
            try {
                return RuntimeSupported(contextual) && contextual.Execute(context);
            }
            finally {
                if (_flushExternal != null) {
                    if (PublicationFlushIsSilent?.Invoke() != true) {
                        SealPublication();
                    }

                    _flushExternal();
                }
            }
        }

        var actor = context.Event.Actor;

        if (actor == null) {
            return box.Execute([]);
        }

        if (!context.Targets.ResolveUsers(context, [actor.VirtualId], WiredSources.Selected).Contains(actor)) {
            return false;
        }

        return box.Execute(actor.GetClient()?.GetHabbo());
    }

    private void RunRuntimeTimersAndSignals()
    {
        var now = _now();

        if (_pollExternal != null) {
            if (PublicationPollIsSilent?.Invoke() != true) {
                SealPublication();
            }

            _pollExternal(now);
        }

        // Every pass polls: the room paces passes, and each timer keeps its own deadlines.
        WiredRuntimeContext? snapshot = null;
        var timers = _items.Values.OfType<IWiredTimedTrigger>().Where(RuntimeSupported)
            .OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).ToArray();

        var dormant = timers.Any(timer => timer is WiredModernTimedTrigger)
            && !_targets!.AllUsers().Any(user => !user.IsBot || user.IsWalking);

        for (var polled = 0; polled < timers.Length && !OutOfBudget(); polled++) {
            _timerCursor %= timers.Length;
            var timer = timers[_timerCursor];
            _timerCursor = (_timerCursor + 1) % timers.Length;

            if (!IsAttached(timer) || dormant && timer is WiredModernTimedTrigger) {
                continue;
            }

            try {
                // Native modern timer polling only reads deadlines; custom callbacks may publish.
                if (timer is not WiredModernTimedTrigger) {
                    SealPublication();
                }

                if (timer.Poll(now) is not { } @event) {
                    continue;
                }

                SealPublication();
                snapshot ??= CreateContext(@event, 0);
                var context = snapshot.Fork(@event, 0);
                context.Trigger = timer;
                SeedEvent(context);

                if (RunRuntimeStack(timer, context, null)) {
                    Flash(timer);
                }
            }
            catch (Exception error) {
                _error(error);
            }
        }

        // Accepted envelopes retain their next trigger and current stack evaluation across passes.
        while (_dispatches.TryPeek(out var pending) && !OutOfBudget()) {
            if (!AdvanceDispatch(pending)) {
                break;
            }

            if (_dispatches.TryPeek(out var head) && ReferenceEquals(head, pending)) {
                RemoveDispatchHead();
            }

            DrainReadySignals();
        }

        DrainReadySignals();
    }

    private bool _drainingSignals;

    private void DrainReadySignals()
    {
        // Continue only ready signal envelopes. An earlier external event retains its FIFO position.
        // An active evaluation may still own the head reservation, so admission must never re-enter it.
        if (_drainingSignals || _draining || _dispatchAdvanceDepth != 0 || _activeFiring != null) {
            return;
        }

        _drainingSignals = true;

        try {
            while (_dispatches.TryPeek(out var pending) && pending.Signal != null && !OutOfBudget()) {
                if (!AdvanceDispatch(pending)) {
                    break;
                }

                if (_dispatches.TryPeek(out var head) && ReferenceEquals(head, pending)) {
                    RemoveDispatchHead();
                }
            }
        }
        finally {
            _drainingSignals = false;
            UpdateFastWork();
        }
    }

    private static bool RuntimeSupported(IWiredConfiguredItem box) => box.Descriptor.Support == WiredBoxSupport.Implemented;

    private void ResetRuntimeTile(int x, int y)
    {
        foreach (var addon in _items.Values.OfType<IWiredContextualAddon>().Where(a => a.Item.GetX == x && a.Item.GetY == y)) {
            addon.Reset();
        }

        foreach (var timer in _items.Values.OfType<IWiredTimedTrigger>().Where(a => a.Item.GetX == x && a.Item.GetY == y)) {
            timer.Reset(_now());
        }
    }

    private void UpdateRuntimeItems()
    {
        foreach (var box in _items.Values) {
            var position = (box.Item.GetX, box.Item.GetY, box.Item.GetZ);

            if (_runtimePositions.TryGetValue(box.Item.Id, out var previous) && previous != position) {
                ResetRuntimeTile(previous.X, previous.Y);
                ResetRuntimeTile(position.GetX, position.GetY);
            }

            _runtimePositions[box.Item.Id] = position;
        }

        foreach (var id in _runtimePositions.Keys.Except(_items.Keys).ToArray()) {
            _runtimePositions.Remove(id);
        }

        UpdateFastWork();
    }

    private void UpdateFastWork()
    {
        var required = _runtimeRoom != null && HasRuntimeWork();
        var next = required ? 1 : 0;

        if (Interlocked.Exchange(ref _fastWork, next) != next) {
            _fastWorkObserver?.Invoke(required);
        }
    }

    private bool HasRuntimeWork()
    {
        if (_dispatches.Count > 0 || _externalFastWork?.Invoke() == true) {
            return true;
        }

        foreach (var box in _items.Values) {
            if (box is IWiredTimedTrigger timer && RuntimeSupported(timer)) {
                return true;
            }
        }

        foreach (var chain in _pending) {
            if (chain.Context.Runtime != null) {
                return true;
            }
        }

        return false;
    }

    private sealed class PendingDispatch(WiredRuntimeEvent @event, int depth, PendingSignal? signal = null, PendingCall? call = null)
    {
        public WiredRuntimeEvent Event { get; } = @event;
        public int Depth { get; } = depth;
        public PendingSignal? Signal { get; } = signal;
        public PendingCall? Call { get; } = call;
        public IWiredContextualTrigger[]? Triggers;
        public WiredRuntimeContext? Root;
        public int Next;
        public RuntimeFiring? Current;
        public IWiredItem[] LegacyTriggers = [];
        public int LegacyNext;
        public IWiredItem[] CapturedBoxes = [];
        public (IWiredItem Box, int X, int Y, double Z, long Generation)[] CapturedPositions = [];
        public Dictionary<IWiredContextualTrigger, (int X, int Y, double Z, long Generation)> TriggerPositions = [];
        public Dictionary<IWiredContextualTrigger, (IWiredItem Box, int X, int Y, double Z, long Generation)[]> RecipientStacks = [];
        public HashSet<IWiredContextualTrigger> CancelledRecipients = [];
        public int Slots = 1;
        public bool Initialized, IsQueued;
        public bool IncludeLegacy = true;
        public bool ResumeImmediately;
        public Modern.Actions.WiredFurniturePublication? Publication;
        public HeadingCollisionProof? CollisionProof;
        public bool Accepted, ConsumedChat;
        public WiredClickResult Click;
    }

    private sealed record PendingCall(IWiredItem Source, WiredRuntimeContext Parent, WiredSelection Selection);
    private sealed record PendingSignal((Item Receiver, long Generation)[] Receivers, WiredRuntimeContext Context, WiredRuntimeContext Parent);
}
