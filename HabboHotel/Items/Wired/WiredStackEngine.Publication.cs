using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Wired;

internal sealed partial class WiredStackEngine
{
    private WiredFurniturePublication? _activePublication;
    private long _publicationEpoch;
    internal bool PublicationBridgesAreTrusted { get; set; }
    internal Func<IWiredItem, bool>? PublicationEvaluationIsSilent { get; set; }
    internal Func<WiredRuntimeEvent, bool>? PublicationObserverIsSilent { get; set; }
    internal Func<bool>? PublicationPollIsSilent { get; set; }
    internal Func<bool>? PublicationFlushIsSilent { get; set; }

    internal void SealExternalPublication() => SealPublication();

    private void SealPublication()
    {
        var publication = _activePublication;
        _activePublication = null;
        publication?.Flush();
    }

    private WiredFurniturePublication? PublicationFor(IWiredItem box, WiredRuntimeContext? context)
    {
        var scheduled = _executingAction;

        if (context == null || scheduled?.Callback != null || box is not WiredModernAction action
            || !action.SupportsPublication(context) || action.PublicationBridge && !PublicationBridgesAreTrusted || scheduled == null) {
            SealPublication();

            return null;
        }

        var inherited = context.InheritedPublication;
        var deadline = scheduled.Chain.FirstResumeAt!.Value + scheduled.DelayMilliseconds;

        if (scheduled.DelayMilliseconds == 0 && inherited is { Open: true } && inherited.Epoch == _publicationEpoch
            && ReferenceEquals(inherited, _activePublication)) {
            return inherited;
        }

        if (_activePublication is { Open: true } active && ReferenceEquals(active.Root, scheduled.Chain)
            && active.Deadline == deadline && active.Epoch == _publicationEpoch) {
            return active;
        }

        SealPublication();
        _activePublication = new(context.Room, scheduled.Chain, deadline, _publicationEpoch);

        return _activePublication;
    }

    private void BeforePublicationDispatch(PendingDispatch pending)
    {
        if (_activePublication == null) {
            return;
        }

        if (pending.Event.Kind == WiredEventKind.Collision) {
            if (pending.CollisionProof is not { } collision || !CollisionStillSilent(collision)) {
                SealPublication();
            }

            return;
        }

        var inherited = pending.Publication;
        var silentObserver = ObserveEvent == null || PublicationObserverIsSilent?.Invoke(pending.Event) == true;
        var empty = pending.Call == null && pending.Signal == null && pending.Current == null
            && pending.Triggers is { Length: 0 } && pending.LegacyTriggers.Length == 0;

        if (silentObserver && (empty || inherited is { Open: true } && ReferenceEquals(inherited, _activePublication))) {
            return;
        }

        SealPublication();
    }

    // Only the concrete room factory binds this path. Custom action publishers keep Dispatch/Mutate.
    internal Action<WiredModernAction> CreateMovementPublicationFactory(IWiredRuntimeOperations operations,
        Action<RoomUser, IEnumerable<Item>, IEnumerable<Item>> walk)
    {
        var room = _runtimeRoom;
        var targets = _targets;
        var runtimeOperations = _operations;
        var attached = _attached;
        var actorPresent = _actorPresent;
        var actorVisit = _actorVisit;
        var observer = ObserveEvent;
        var classifier = PublicationObserverIsSilent;
        var evaluation = PublicationEvaluationIsSilent;
        var poll = _pollExternal;
        var flush = _flushExternal;
        var needsFast = _externalFastWork;
        var silentPoll = PublicationPollIsSilent;
        var silentFlush = PublicationFlushIsSilent;
        var bridges = PublicationBridgesAreTrusted;
        bool BindingsCurrent() => ReferenceEquals(room, _runtimeRoom) && ReferenceEquals(targets, _targets)
            && ReferenceEquals(runtimeOperations, _operations) && ReferenceEquals(operations, runtimeOperations)
            && ReferenceEquals(attached, _attached)
            && ReferenceEquals(actorPresent, _actorPresent) && ReferenceEquals(actorVisit, _actorVisit)
            && ReferenceEquals(observer, ObserveEvent) && ReferenceEquals(classifier, PublicationObserverIsSilent)
            && ReferenceEquals(evaluation, PublicationEvaluationIsSilent) && ReferenceEquals(poll, _pollExternal)
            && ReferenceEquals(flush, _flushExternal) && ReferenceEquals(needsFast, _externalFastWork)
            && ReferenceEquals(silentPoll, PublicationPollIsSilent) && ReferenceEquals(silentFlush, PublicationFlushIsSilent)
            && bridges == PublicationBridgesAreTrusted;

        return action =>
        {
            BindHeadingCollision(action, operations, BindingsCurrent);
            BindCarryPublication(action, operations, walk, BindingsCurrent);
        };
    }

    private void BindCarryPublication(WiredModernAction action, IWiredRuntimeOperations operations,
        Action<RoomUser, IEnumerable<Item>, IEnumerable<Item>> walk, Func<bool> bindingsCurrent)
    {
        var now = _now;
        bool Current(WiredRuntimeContext context) => bindingsCurrent() && ReferenceEquals(now, _now)
            && RoomOwnerScope.IsOwner(context.Room) && ReferenceEquals(context.Room, _runtimeRoom)
            && ReferenceEquals(context, _runtimeContext) && ReferenceEquals(context.Operations, operations)
            && ReferenceEquals(_executingAction?.Box, action)
            && _items.TryGetValue(action.Item.Id, out var registered) && ReferenceEquals(action, registered)
            && action.WalkBindingIsCurrent(walk);
        WiredFurniturePublication? Token(WiredRuntimeContext context) => Current(context)
            && context.Publication is { Open: true } publication && ReferenceEquals(publication, _activePublication)
            && publication.Epoch == _publicationEpoch && action.SupportsPublication(context) ? publication : null;
        void Seal(WiredRuntimeContext context)
        {
            context.Publication?.Flush();
            SealPublication();
        }

        action.BindCarryPublication((context, actors) =>
        {
            var token = Token(context);

            if (token == null || actors.Any(token.ContainsActor)) {
                // Split before any later furniture commit or actor style, even for equal styles.
                Seal(context);
            }
        }, (context, actor, movement, style) =>
        {
            var token = Token(context);

            if (token != null && context.UserIdentity.TryGetValue(actor.VirtualId, out var captured)
                && ReferenceEquals(actor, captured) && token.AppendAvatar(actor, movement, style)) {
                // Append owns publication even if the original hint reentrantly sealed the token.
                return true;
            }

            Seal(context);

            return false;
        });
    }

    private void BindHeadingCollision(WiredModernAction action, IWiredRuntimeOperations operations, Func<bool> bindingsCurrent)
    {
        // The readonly clock binding belongs to this factory-created action.
        var now = _now;
        bool BindingIsCurrent() => bindingsCurrent() && ReferenceEquals(now, _now);
        action.BindHeadingCollision((context, evt) => Pass<bool?>(() =>
        {
            if (!ReferenceEquals(context, _runtimeContext) || !ReferenceEquals(context.Room, _runtimeRoom)
                || !ReferenceEquals(context.Operations, operations) || !ReferenceEquals(_executingAction?.Box, action)
                || !_items.TryGetValue(action.Item.Id, out var registered) || !ReferenceEquals(action, registered)) {
                return null;
            }

            HeadingCollisionProof? proof = null;

            // No replaceable predicate, accessor or observer is invoked before binding/registry checks.
            if (BindingIsCurrent() && context.Publication is { Open: true } publication
                && ReferenceEquals(publication, _activePublication) && publication.Epoch == _publicationEpoch
                && CollisionRegistryUnchanged() && action.SupportsPublication(context)
                && evt is { Kind: WiredEventKind.Collision, EventItem: { } source, Actor: { } actor }
                && context.FurniIdentity.TryGetValue(source.Id, out var sourceIdentity) && ReferenceEquals(source, sourceIdentity)
                && context.UserIdentity.TryGetValue(actor.VirtualId, out var actorIdentity) && ReferenceEquals(actor, actorIdentity)) {
                proof = new(publication, BindingIsCurrent, source, source.Placement, source.Definition,
                    actor, actor.Movement.LifetimeId, _stackSnapshot, _items.Values.Cast<IWiredConfiguredItem>()
                        .Select(box => (box, box.Configuration)).ToArray());

                if (!CollisionStillSilent(proof)) {
                    proof = null;
                }
            }

            if (proof == null) {
                context.Publication?.Flush();
                SealPublication();
            }

            return EnqueueCore(evt, collision: proof, sealOnRejection: true);
        }));
    }

    private bool CollisionRegistryUnchanged()
    {
        // Reject dirty/opaque/legacy registries BEFORE normal RefreshStacks can invoke callbacks.
        if (_stacksDirty || _items.Count != _stackSnapshot.Length || _fastWork != 1) {
            return false;
        }

        var index = 0;
        var keepsFast = false;

        foreach (var box in _items.Values) {
            var type = box.GetType();

            if (type != typeof(WiredModernAction) && type != typeof(WiredModernCondition)
                && type != typeof(WiredSelectorBox) && type != typeof(WiredAddonBox)
                && type != typeof(WiredModernTrigger) && type != typeof(WiredModernTimedTrigger)) {
                return false;
            }

            keepsFast |= type == typeof(WiredModernTimedTrigger) && RuntimeSupported((IWiredConfiguredItem)box);
            var previous = _stackSnapshot[index++];

            if (!ReferenceEquals(box, previous.Box) || !_attached(box)
                || (box.Item.GetX, box.Item.GetY, box.Item.GetZ) != (previous.X, previous.Y, previous.Z)
                || box is WiredModernTrigger trigger && trigger.Events.Contains(WiredEventKind.Collision)) {
                return false;
            }
        }

        // Even after the empty envelope leaves the queue, this known timer keeps fast-work unchanged.
        // Otherwise an opaque fast-work observer could run before the open segment is sealed.
        return keepsFast;
    }

    private bool CollisionStillSilent(HeadingCollisionProof proof) => proof.BindingsCurrent()
        && proof.Publication.Open && ReferenceEquals(proof.Publication, _activePublication)
        && proof.Publication.Epoch == _publicationEpoch && CollisionRegistryUnchanged()
        && ReferenceEquals(proof.RegistrySnapshot, _stackSnapshot) && proof.Boxes.Length == _items.Count
        && proof.Boxes.All(entry => _items.TryGetValue(entry.Box.Item.Id, out var box) && ReferenceEquals(entry.Box, box)
            && ReferenceEquals(entry.Configuration, entry.Box.Configuration))
        && proof.Source.Placement == proof.Placement && ReferenceEquals(proof.Source.Definition, proof.Definition)
        && ReferenceEquals(_runtimeRoom!.GetRoomItemHandler().GetItem(proof.Source.Id), proof.Source)
        && proof.Actor.Movement.LifetimeId == proof.ActorLifetime
        && ReferenceEquals(_runtimeRoom.GetRoomUserManager().GetRoomUserByVirtualId(proof.Actor.VirtualId), proof.Actor);

    private sealed record HeadingCollisionProof(WiredFurniturePublication Publication, Func<bool> BindingsCurrent,
        Item Source, long Placement, ItemDefinition Definition, RoomUser Actor, long ActorLifetime, object RegistrySnapshot,
        (IWiredConfiguredItem Box, WiredConfiguration Configuration)[] Boxes);
}
