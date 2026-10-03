using System.Drawing;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Rooms.Rollers;

namespace Plus.HabboHotel.Rooms.PathFinding;

// V2 rules for the shared roller planner: Roller-purpose CanStep, claim-aware cargo, collective R
// reservations on every landing tile, one publish, then ExactZ binding to the carried Z.
internal sealed class RollerTransport(RoomNavigation navigation, MovementContext context, ForcePlacementService placement)
    : IRollerTransportEngine
{
    private const TargetOccupancy Anything = (TargetOccupancy)127;
    private NavGrid Grid => navigation.Grid;

    public bool CanRide(RoomUser actor) => actor.Movement.State == NavState.Active && !actor.IsWalking
        && actor.Movement.PendingCount == 0;

    public bool AdmitsActor(RollerMove move, RollerDepartures departing)
    {
        var to = move.Destination;
        if (!Grid.InBounds(to.X, to.Y)) return false;
        var actor = move.Actor!; var slot = Grid.Tile(to.X, to.Y);
        var profile = context.Profiles.Refresh(actor);
        var from = new NavPosition(move.Origin.X, move.Origin.Y, move.SourceZ);
        var occupancy = context.OccupancyAt(actor, slot, departing.Users);
        var rules = new MovementRules(Grid, navigation.Settings);
        var result = departing.IsEmpty || departing.Roller?.Definition.Walkable != true
            ? rules.CanStep(profile, from, Grid.Position(slot), StepPurpose.Roller, OccupancyView.Execution, occupancy)
            : rules.CanRollOntoVacatedRoller(profile, from, Grid.Position(slot), occupancy);
        return result.Ok;
    }

    // Furniture may not land where another actor stands, walks or holds any claim, on any footprint tile.
    public bool AdmitsCargo(RollerMove move, IRollerDepartureView departures)
        => CargoFootprint(move).All(tile => !Grid.InBounds(tile.X, tile.Y)
            || (context.Claims.OccupancyAt(Grid.Tile(tile.X, tile.Y), ClaimLedger.NoGroup, departures.At(tile).Users) & Anything) == 0);

    // One R claim per tile the group lands on, with that tile's own departures excluded; all or nothing.
    public bool Reserve(TransportGroup group)
    {
        var reserved = new List<Action>();
        foreach (var (tile, actor) in LandingTiles(group))
        {
            var release = TryReserve(tile, actor, group.At(tile));
            if (release != null) { reserved.Add(release); continue; }
            foreach (var undo in reserved) undo();
            return false;
        }
        return true;
    }

    public void CommitActors(IReadOnlyList<RollerMove> moves)
    {
        foreach (var move in moves)
        {
            var actor = move.Actor!;
            placement.Relocate(actor, move.Destination.X, move.Destination.Y, move.CarriedZ,
                actor.Movement.Commands.Read()?.Sequence ?? 0);
            actor.IsRolling = true; actor.RollerDelay = 1;
        }
    }

    public void Publish(IReadOnlyList<RollerMove> moves)
    {
        navigation.ApplyDirty();
        foreach (var move in moves)
        {
            placement.Bind(move.Actor!, move.CarriedZ, ForceResolution.ExactZ);
            context.RefreshMembership(move.Actor!);
        }
    }

    public void Land(RollerMove move) => context.TransportLandings.Trigger(move.Actor!, move.Destination, move.Roller);

    // Actor destinations plus every cargo's whole destination footprint. An arriving actor owns its tile.
    private static Dictionary<Point, RoomUser?> LandingTiles(TransportGroup group)
    {
        var tiles = new Dictionary<Point, RoomUser?>();
        foreach (var move in group.Moves.Where(move => move.Actor != null)) tiles.TryAdd(move.Destination, move.Actor);
        foreach (var tile in group.Moves.Where(move => move.Cargo != null).SelectMany(CargoFootprint)) tiles.TryAdd(tile, null);
        return tiles;
    }

    private static IEnumerable<Point> CargoFootprint(RollerMove move)
        => WiredRoomOperations.Footprint(move.Cargo!, move.Destination.X, move.Destination.Y, move.Cargo!.Rotation);

    private Action? TryReserve(Point tile, RoomUser? actor, RollerDepartures departing)
    {
        if (!Grid.InBounds(tile.X, tile.Y)) return () => { };
        var slot = Grid.Tile(tile.X, tile.Y);
        if (actor == null)
            return context.Claims.TryReserveCargo(slot, Anything, departing.Users) ? () => context.Claims.ReleaseCargo(slot) : null;
        var mask = ClaimMatrix.BlockingMask(context.Profiles.Refresh(actor), Grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);
        return context.Claims.TryClaim(actor, slot, ClaimKind.Roller, mask, departing.Users)
            ? () => context.Claims.ReleaseRollers(actor) : null;
    }
}
