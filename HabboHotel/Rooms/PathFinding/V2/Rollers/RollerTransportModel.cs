using System.Drawing;
using Plus.Communication.Packets;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

// V2 roller identity. The legacy Item.IsRoller stays the original, never-assigned property.
internal static class RollerIdentity
{
    internal static bool IsRoller(Item item) => item.Definition.InteractionType == InteractionType.Roller;
}

// The state a move was planned from. Any change before its group commits makes the group stale.
internal readonly record struct RollerSnapshot(int RollerRotation, double RollerZ, double SourceZ, long ActorRevision,
    int CargoRotation = 0);

// One mover leaving a roller: cargo or the riding actor. Origin, Z and the slide packet are
// captured when the cycle snapshots its loads (§14.9 a).
internal sealed record RollerMove(Item Roller, Point Origin, Point Destination, RollerSnapshot Snapshot,
    double CarriedZ, Item? Cargo, RoomUser? Actor, IServerPacket Slide)
{
    internal double SourceZ => Snapshot.SourceZ;
}

// Everything a roller would carry this cycle. Reference identity keys the plan.
internal sealed class RollerLoad(Item roller, Point destination, IReadOnlyList<RollerMove> moves)
{
    internal Item Roller { get; } = roller;
    internal Point Origin { get; } = new(roller.GetX, roller.GetY);
    internal Point Destination { get; } = destination;
    internal IReadOnlyList<RollerMove> Moves { get; } = moves;
}

// Confirmed departures by origin tile, as seen by plan-local validation.
internal interface IRollerDepartureView
{
    RollerDepartures At(Point tile);
}

internal enum TransportGroupKind
{
    Single, Chain, Loop
}

// An indivisible transport: it commits completely or not at all.
internal sealed class TransportGroup : IRollerDepartureView
{
    private readonly Dictionary<Point, RollerDepartures> _departures;

    internal TransportGroup(TransportGroupKind kind, IReadOnlyList<RollerMove> moves)
    {
        Kind = kind;
        Moves = moves;
        FirstRollerId = moves.Min(move => move.Roller.Id);
        _departures = moves.GroupBy(move => move.Origin).ToDictionary(tile => tile.Key, RollerDepartures.Of);
    }

    internal TransportGroupKind Kind { get; }
    internal IReadOnlyList<RollerMove> Moves { get; }
    internal uint FirstRollerId { get; }

    public RollerDepartures At(Point tile) => _departures.GetValueOrDefault(tile, RollerDepartures.None);
}

// Confirmed departures from one tile. Plan-local validation ignores only these occupants.
internal sealed class RollerDepartures
{
    internal static readonly RollerDepartures None = new(null, new HashSet<uint>(),
        new HashSet<RoomUser>(ReferenceEqualityComparer.Instance));

    private RollerDepartures(Item? roller, IReadOnlySet<uint> items, IReadOnlySet<RoomUser> users)
    {
        Roller = roller;
        Items = items;
        Users = users;
    }

    internal Item? Roller { get; }
    internal IReadOnlySet<uint> Items { get; }
    internal IReadOnlySet<RoomUser> Users { get; }
    internal bool IsEmpty => Items.Count == 0 && Users.Count == 0;

    internal bool AllUsersLeave(IEnumerable<RoomUser> occupants) => occupants.All(Users.Contains);

    // Solid cargo leaving the tile rebuilds its map cell, which ends any explicit floor status there
    // (the legacy lock lifetime v2 follows); a lock under it is attributed to the cargo.
    internal bool ReleasesFloorStatus(Room room)
    {
        var map = room.GetGameMap();
        var items = room.GetRoomItemHandler();

        return Items.Select(items.GetItem).Any(cargo => cargo != null && map.ItemWalkState(cargo) == 0);
    }

    internal static RollerDepartures Of(IEnumerable<RollerMove> moves)
    {
        var list = moves.ToList();

        if (list.Count == 0) {
            return None;
        }

        return new(list[0].Roller, list.Where(move => move.Cargo != null).Select(move => move.Cargo!.Id).ToHashSet(),
            list.Where(move => move.Actor != null).Select(move => move.Actor!).ToHashSet<RoomUser>(ReferenceEqualityComparer.Instance));
    }
}
