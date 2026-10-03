using System.Drawing;
using Plus.HabboHotel.Rooms.Rollers;

namespace Plus.HabboHotel.Rooms.PathFinding;

// V2 rules for the shared roller planner: Roller-purpose CanStep, claim-aware cargo, collective R
// reservations on every destination, one publish, then ExactZ binding to the carried Z.
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

    // Furniture may not land where another actor stands, walks or holds any claim.
    public bool AdmitsCargo(RollerMove move, RollerDepartures departing)
    {
        var to = move.Destination;
        return !Grid.InBounds(to.X, to.Y)
            || (context.Claims.OccupancyAt(Grid.Tile(to.X, to.Y), ClaimLedger.NoGroup, departing.Users) & Anything) == 0;
    }

    public bool Reserve(TransportGroup group)
    {
        var reserved = new List<Action>();
        foreach (var destination in group.Moves.GroupBy(move => move.Destination))
        {
            var release = TryReserve(destination.Key, destination.ToList(), group.At(destination.Key));
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

    // One R claim per destination: the arriving actor owns it, otherwise the cargo reservation does.
    private Action? TryReserve(Point destination, IReadOnlyList<RollerMove> arriving, RollerDepartures departing)
    {
        if (!Grid.InBounds(destination.X, destination.Y)) return () => { };
        var slot = Grid.Tile(destination.X, destination.Y);
        if (arriving.FirstOrDefault(move => move.Actor != null)?.Actor is not { } actor)
            return context.Claims.TryReserveCargo(slot, Anything, departing.Users) ? () => context.Claims.ReleaseCargo(slot) : null;
        var mask = ClaimMatrix.BlockingMask(context.Profiles.Refresh(actor), Grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);
        return context.Claims.TryClaim(actor, slot, ClaimKind.Roller, mask, departing.Users)
            ? () => context.Claims.ReleaseRollers(actor) : null;
    }
}
