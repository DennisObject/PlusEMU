using System.Drawing;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Rooms.Rollers;

// Legacy actor rules for the shared roller planner: the legacy roller step check, the legacy
// walkability writes and the legacy Wired landing hooks.
internal sealed class LegacyRollerTransport(Room room, RoomItemHandling handler) : IRollerTransportEngine
{
    public bool CanRide(RoomUser actor) => !actor.IsWalking;

    public bool AdmitsActor(RollerMove move, RollerDepartures departing)
    {
        var map = room.GetGameMap(); var to = move.Destination;
        if (StructurallyLocked(to, departing)) return false;
        if (departing.IsEmpty)
            return map.IsValidStep(new Vector2D(move.Origin.X, move.Origin.Y), new Vector2D(to.X, to.Y), true, false, true)
                && map.GetFloorStatus(to) != 0;
        // A vacated roller tile is its bare roller surface: clearance proved nothing staying rises above it.
        return departing.Roller?.Definition.Walkable == true
            && (room.RoomBlockingEnabled || departing.AllUsersLeave(map.GetRoomUsers(to)));
    }

    // Legacy has no claims; the shared furniture rules are the whole cargo admission.
    public bool AdmitsCargo(RollerMove move, IRollerDepartureView departures) => true;

    public bool Reserve(TransportGroup group) => true;

    // A structural 0 is an explicit floor lock unless departing solid cargo explains it: that cargo's
    // departure rebuilds the cell, which (as legacy always did) erases any lock there. Avatars never
    // write the structural map, so a lock under a departing user survives until the cell is rebuilt.
    private bool StructurallyLocked(Point tile, RollerDepartures departing)
    {
        var map = room.GetGameMap();
        return map.StructuralState(tile) == 0 && !departing.Items.Select(handler.GetItem)
            .Any(cargo => cargo != null && map.ItemWalkState(cargo) == 0);
    }

    // Clear every origin before marking destinations so a rotating loop keeps its occupied tiles.
    public void CommitActors(IReadOnlyList<RollerMove> moves)
    {
        var map = room.GetGameMap();
        foreach (var move in moves)
        {
            map.UpdateUserMovement(move.Origin, move.Destination, move.Actor!);
            map.GameMap[move.Origin.X, move.Origin.Y] = 1;
        }
        foreach (var move in moves)
        {
            var actor = move.Actor!;
            actor.X = move.Destination.X; actor.Y = move.Destination.Y; actor.Z = move.CarriedZ;
            map.GameMap[actor.X, actor.Y] = 0;
            actor.IsRolling = true; actor.RollerDelay = 1;
        }
    }

    public void Publish(IReadOnlyList<RollerMove> moves) { }

    public void Land(RollerMove move) => handler.TriggerRollerLanding(move.Actor!, move.Destination, move.Roller.Id);
}
