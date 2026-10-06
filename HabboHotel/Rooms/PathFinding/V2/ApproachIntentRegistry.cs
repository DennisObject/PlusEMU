namespace Plus.HabboHotel.Rooms.PathFinding;

public static class ApproachActionKind
{
    public const int VendingMachine = 1;
    public const int Teleporter = 2;
    public const int Hopper = 3;
}

// An approach descriptor bound to the command that carried it; replans of the same command keep this identity.
internal sealed record ApproachIntent(long LifetimeId, long Sequence, ApproachDescriptor Descriptor, SurfaceRef Surface);

// Holds no locks and no interactor state: the walk to the approach tile owns nothing until completion.
internal sealed class ApproachIntentRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<RoomUser, ApproachIntent> _intents = new(ReferenceEqualityComparer.Instance);

    // Consumption of any command replaces the actor's previous intent; one without an approach, or whose
    // approach surface is unresolved, just clears it. The owner supplies the surface it resolved after applying geometry.
    public void Bind(RoomUser actor, MoveCommand command, SurfaceRef? resolved = null)
    {
        lock (_gate) {
            if (command.Approach is { } approach && (resolved ?? approach.ApproachSurfaceRef) is { Tile: >= 0 } surface) {
                _intents[actor] = new(actor.Movement.LifetimeId, command.Sequence, approach, surface);
            }
            else {
                _intents.Remove(actor);
            }
        }
    }

    public ApproachIntent? Peek(RoomUser actor)
    {
        lock (_gate) {
            return _intents.GetValueOrDefault(actor);
        }
    }

    // Removal and read are one step, so an intent can be taken by exactly one caller.
    public ApproachIntent? Consume(RoomUser actor)
    {
        lock (_gate) {
            return _intents.Remove(actor, out var intent) ? intent : null;
        }
    }

    public void Cancel(RoomUser actor)
    {
        lock (_gate) {
            _intents.Remove(actor);
        }
    }

    public void CancelItem(uint itemId)
    {
        lock (_gate) {
            var stale = _intents.Where(pair => pair.Value.Descriptor.ItemId == itemId).Select(pair => pair.Key).ToList();

            foreach (var actor in stale) {
                _intents.Remove(actor);
            }
        }
    }

    public void Clear()
    {
        lock (_gate) {
            _intents.Clear();
        }
    }
}
