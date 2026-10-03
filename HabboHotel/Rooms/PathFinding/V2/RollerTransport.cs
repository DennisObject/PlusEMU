using Plus.HabboHotel.Rooms.Rollers;

namespace Plus.HabboHotel.Rooms.PathFinding;

// V2 actor rules for the shared roller planner: Roller-purpose CanStep, collective R claims,
// one publish, then ExactZ binding to the carried Z (off-graph when no surface matches).
internal sealed class RollerTransport(RoomNavigation navigation, MovementContext context, ForcePlacementService placement)
    : IRollerTransportEngine
{
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

    public bool Reserve(TransportGroup group)
    {
        var reserved = new List<RoomUser>();
        foreach (var move in group.Moves.Where(move => move.Actor != null))
        {
            if (!TryReserve(move, group.DeparturesFrom(move.Destination)))
            {
                foreach (var actor in reserved) context.Claims.ReleaseRollers(actor);
                return false;
            }
            reserved.Add(move.Actor!);
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

    private bool TryReserve(RollerMove move, RollerDepartures departing)
    {
        var slot = Grid.Tile(move.Destination.X, move.Destination.Y);
        var profile = context.Profiles.Refresh(move.Actor!);
        var mask = ClaimMatrix.BlockingMask(profile, Grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);
        return context.Claims.TryClaim(move.Actor!, slot, ClaimKind.Roller, mask, departing.Users);
    }
}
