using System.Drawing;
using Plus.Communication.Packets;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Rollers;

// One mover leaving a roller: cargo or the riding actor. Origin, Z and the slide packet are
// captured when the cycle snapshots its loads (§14.9 a).
internal sealed record RollerMove(Item Roller, Point Origin, Point Destination, double SourceZ,
    double CarriedZ, Item? Cargo, RoomUser? Actor, IServerPacket Slide);

// Everything a roller would carry this cycle. Reference identity keys the plan.
internal sealed class RollerLoad(Item roller, Point destination, IReadOnlyList<RollerMove> moves)
{
    internal Item Roller { get; } = roller;
    internal Point Origin { get; } = new(roller.GetX, roller.GetY);
    internal Point Destination { get; } = destination;
    internal IReadOnlyList<RollerMove> Moves { get; } = moves;
}

internal enum TransportGroupKind { Single, Chain, Loop }

// An indivisible transport: it commits completely or not at all.
internal sealed record TransportGroup(TransportGroupKind Kind, IReadOnlyList<RollerMove> Moves)
{
    internal uint FirstRollerId => Moves.Min(move => move.Roller.Id);

    internal RollerDepartures DeparturesFrom(Point tile) => RollerDepartures.Of(Moves.Where(move => move.Origin == tile));
}

// Confirmed departures from one tile. Plan-local validation ignores only these occupants.
internal sealed class RollerDepartures
{
    internal static readonly RollerDepartures None = new(null, new HashSet<uint>(),
        new HashSet<RoomUser>(ReferenceEqualityComparer.Instance));

    private RollerDepartures(Item? roller, IReadOnlySet<uint> items, IReadOnlySet<RoomUser> users)
    {
        Roller = roller; Items = items; Users = users;
    }

    internal Item? Roller { get; }
    internal IReadOnlySet<uint> Items { get; }
    internal IReadOnlySet<RoomUser> Users { get; }
    internal bool IsEmpty => Items.Count == 0 && Users.Count == 0;

    internal bool AllUsersLeave(IEnumerable<RoomUser> occupants) => occupants.All(Users.Contains);

    internal static RollerDepartures Of(IEnumerable<RollerMove> moves)
    {
        var list = moves.ToList();
        if (list.Count == 0) return None;
        return new(list[0].Roller, list.Where(move => move.Cargo != null).Select(move => move.Cargo!.Id).ToHashSet(),
            list.Where(move => move.Actor != null).Select(move => move.Actor!).ToHashSet<RoomUser>(ReferenceEqualityComparer.Instance));
    }
}
