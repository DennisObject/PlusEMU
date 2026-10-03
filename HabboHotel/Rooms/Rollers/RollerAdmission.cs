using System.Drawing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Modern;

namespace Plus.HabboHotel.Rooms.Rollers;

internal interface IRollerAdmission
{
    bool Admits(RollerMove move, IRollerDepartureView departures);
}

// Plan-local admission: confirmed departures stop blocking, everything else still does.
internal sealed class RollerAdmission(Room room, IRollerTransportEngine engine) : IRollerAdmission
{
    public bool Admits(RollerMove move, IRollerDepartureView departures)
    {
        var destination = move.Destination;
        var departing = departures.At(destination);
        if (!room.GetGameMap().CanRollItemHere(destination.X, destination.Y)
            || !NextRollerClear(destination, departing)) return false;
        return move.Cargo is { } cargo
            ? AdmitsCargo(cargo, move, departures) && engine.AdmitsCargo(move, departures)
            : engine.AdmitsActor(move, departing);
    }

    // Next-roller clearance: nothing that stays may rise above the destination roller.
    private bool NextRollerClear(Point tile, RollerDepartures departing)
    {
        var items = room.GetGameMap().GetAllRoomItemForSquare(tile.X, tile.Y);
        var rollers = items.Where(item => item.IsRoller).ToList();
        if (rollers.Count == 0) return true;
        var top = rollers.Max(roller => roller.TotalHeight);
        return items.All(item => item.TotalHeight <= top || departing.Items.Contains(item.Id));
    }

    // Preflights every rejection of the furniture setter, so the group commit itself cannot fail.
    private bool AdmitsCargo(Item cargo, RollerMove move, IRollerDepartureView departures)
    {
        var destination = move.Destination;
        return departures.At(destination).AllUsersLeave(room.GetGameMap().GetRoomUsers(destination))
            && room.GetRoomItemHandler().CanMoveFloorItem(cargo, destination.X, destination.Y, move.CarriedZ,
                cargo.IsTemporary ? PlanLocalCollision(cargo, destination, departures) : null);
    }

    // Temporary cargo checks its whole footprint; departing occupants there no longer collide.
    private static WiredCollisionPolicy PlanLocalCollision(Item cargo, Point destination, IRollerDepartureView departures)
    {
        var footprint = WiredRoomOperations.Footprint(cargo, destination.X, destination.Y, cargo.Rotation)
            .Select(departures.At).ToList();
        return new(footprint.SelectMany(tile => tile.Items).ToHashSet(),
            footprint.SelectMany(tile => tile.Users).Select(user => user.VirtualId).ToHashSet(), new HashSet<uint>());
    }
}
