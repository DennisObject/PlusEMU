using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Single completion entry for a final landing and a validated AlreadyThere.
internal sealed class ApproachCompletion(Room room, RoomNavigation navigation, ApproachIntentRegistry registry,
    Func<Item, IApproachInteractor?>? resolve = null)
{
    private readonly Func<Item, IApproachInteractor?> _resolve = resolve ?? (item => item.Interactor as IApproachInteractor);

    // The intent is consumed before any recheck, so a re-entrant call can never start it twice.
    public bool Complete(RoomUser actor, long landingRevision)
    {
        if (registry.Consume(actor) is not { } intent) {
            return false;
        }

        return StillValid(actor, intent, landingRevision) && Start(actor, intent.Descriptor);
    }

    private bool StillValid(RoomUser actor, ApproachIntent intent, long landingRevision)
    {
        var state = actor.Movement;

        return state.State == NavState.Active && state.LifetimeId == intent.LifetimeId
            && state.LocationRevision == landingRevision
            && !(state.Commands.Read()?.Sequence > intent.Sequence)
            && state.CurrentRef == intent.Surface
            && ItemUnchanged(intent.Descriptor) && HasPermission(actor);
    }

    private bool ItemUnchanged(ApproachDescriptor descriptor)
        => navigation.Inputs.Read(descriptor.ItemId) is { Removed: false } record
            && record.Version == descriptor.ItemRecordVersion
            && room.GetRoomItemHandler().GetItem(descriptor.ItemId) is { } item
            && item.StateGeneration == descriptor.StateGeneration;

    private bool HasPermission(RoomUser actor)
        => !actor.IsBot && actor.GetClient()?.GetHabbo() is { } habbo && ReferenceEquals(habbo.CurrentRoom, room);

    // Ownership of busy/lock fields moves to the interactor's own timed state machine here.
    private bool Start(RoomUser actor, ApproachDescriptor descriptor)
    {
        var item = room.GetRoomItemHandler().GetItem(descriptor.ItemId);
        var interactor = item == null ? null : _resolve(item);

        return interactor != null && interactor.ActionKind == descriptor.ActionKind
            && interactor.StartFromApproach(item!, actor);
    }
}
