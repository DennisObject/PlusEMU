using System.Drawing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Modern;

namespace Plus.HabboHotel.Rooms.Rollers;

internal interface IRollerAdmission
{
    bool Admits(RollerMove move, RollerDepartures departing);
}

// Plan-local admission: confirmed departures from the destination stop blocking, everything else still does.
internal sealed class RollerAdmission(Room room, IRollerTransportEngine engine) : IRollerAdmission
{
    public bool Admits(RollerMove move, RollerDepartures departing)
    {
        var destination = move.Destination;
        if (!room.GetGameMap().CanRollItemHere(destination.X, destination.Y)
            || !NextRollerClear(destination, departing)) return false;
        return move.Cargo is { } cargo ? AdmitsCargo(cargo, move, departing) : engine.AdmitsActor(move, departing);
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

    private bool AdmitsCargo(Item cargo, RollerMove move, RollerDepartures departing)
        => departing.AllUsersLeave(room.GetGameMap().GetRoomUsers(move.Destination))
            && (!cargo.IsTemporary || WiredRoomOperations.CanMoveItem(room, cargo, move.Destination.X,
                move.Destination.Y, cargo.Rotation, move.CarriedZ));
}
