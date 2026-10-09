using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Output only, for one engine-owned continuous movement segment. Room commits remain immediate.</summary>
internal sealed class WiredFurniturePublication(Room room, object root, long deadline, long epoch)
{
    private readonly Dictionary<Item, (WiredMovementComposer Movement, WiredMoveStyleComposer Style, long Placement, ItemDefinition Definition)> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<Item> _order = [];
    private readonly List<(RoomUser Actor, long Lifetime, WiredMovementComposer Movement)> _avatars = [];
    internal object Root { get; } = root;
    internal long Deadline { get; } = deadline;
    internal long Epoch { get; } = epoch;
    internal bool Open { get; private set; } = true;

    internal bool Append(Item item, WiredMovementComposer movement, WiredMoveStyleComposer style)
    {
        if (!Open || !ReferenceEquals(item.GetRoom(), room) || !ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item)) {
            return false;
        }

        if (_entries.TryGetValue(item, out var previous)) {
            if (previous.Placement != item.Placement || !ReferenceEquals(previous.Definition, item.Definition)) {
                Flush();

                return false;
            }

            movement = movement with
            {
                FromX = previous.Movement.FromX,
                FromY = previous.Movement.FromY,
                FromZ = previous.Movement.FromZ
            };
        }

        if (!_entries.ContainsKey(item)) {
            _order.Add(item);
        }

        _entries[item] = (movement, style, item.Placement, item.Definition);

        return true;
    }

    internal bool ContainsActor(RoomUser actor) => _avatars.Any(entry => ReferenceEquals(entry.Actor, actor));

    internal bool AppendAvatar(RoomUser actor, WiredMovementComposer movement, WiredMoveStyleComposer style)
    {
        var lifetime = actor.Movement.LifetimeId;

        if (!Open || ContainsActor(actor) || !LiveActor(actor, lifetime)) {
            return false;
        }

        _avatars.Add((actor, lifetime, movement));
        // Retention owns the animation even if this original hint's callback seals the segment.
        room.SendPacket(style);

        return true;
    }

    private bool LiveActor(RoomUser actor, long lifetime) => actor.Movement.State != NavState.Removing
        && actor.Movement.LifetimeId == lifetime
        && ReferenceEquals(room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor);

    internal void Flush()
    {
        if (!Open) {
            return;
        }

        Open = false;
        var live = _order.Where(item => ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item)
                && ReferenceEquals(item.GetRoom(), room) && item.Placement == _entries[item].Placement
                && ReferenceEquals(item.Definition, _entries[item].Definition))
            .Select(item => _entries[item]).ToArray();
        var avatars = _avatars.ToArray();
        _entries.Clear();
        _order.Clear();
        _avatars.Clear();

        foreach (var entry in live) {
            room.SendPacket(entry.Style);
        }

        // A style callback can remove/reuse an actor; validate again immediately before the batch.
        var movements = live.Select(entry => entry.Movement)
            .Concat(avatars.Where(entry => LiveActor(entry.Actor, entry.Lifetime)).Select(entry => entry.Movement)).ToArray();

        if (movements.Length > 0) {
            room.SendPacket(new WiredMovementBatchComposer(movements));
        }
    }
}
