using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Rollers;

// Revalidates a group just before it commits: snapshots must still hold, and the final geometry is
// checked with plan-local occupancy that ignores only the group's own confirmed departures.
internal sealed class TransportGroupValidator(Room room, IRollerTransportEngine engine, IRollerAdmission admission)
{
    internal bool IsValid(TransportGroup group)
        => group.Moves.All(Unchanged)
            && group.Moves.All(move => admission.Admits(move, group.DeparturesFrom(move.Destination)));

    private bool Unchanged(RollerMove move)
    {
        if (!InPlace(move.Roller, move)) return false;
        if (move.Cargo is { } cargo) return InPlace(cargo, move) && cargo.GetZ == move.SourceZ;
        var actor = move.Actor!;
        return ReferenceEquals(room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor)
            && actor.X == move.Origin.X && actor.Y == move.Origin.Y && engine.CanRide(actor);
    }

    private bool InPlace(Item item, RollerMove move)
        => ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item)
            && item.GetX == move.Origin.X && item.GetY == move.Origin.Y;
}
