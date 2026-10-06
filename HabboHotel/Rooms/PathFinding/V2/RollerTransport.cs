using Plus.HabboHotel.Items;
using System.Drawing;
using Plus.HabboHotel.Items.Wired.Modern;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Actor and claim rules for the v2 roller planner: Roller-purpose CanStep, claim-aware cargo, collective R
// reservations on every landing tile, one publish, then ExactZ binding to the carried Z.
internal sealed class RollerTransport(RoomNavigation navigation, MovementContext context, ForcePlacementService placement)
{
    private const TargetOccupancy Anything = (TargetOccupancy)127;
    private NavGrid Grid => navigation.Grid;

    public bool CanRide(RoomUser actor) => actor.Movement.State == NavState.Active && !actor.IsWalking
        && actor.Movement.PendingCount == 0;

    public bool RestsOnRoller(Item roller, RoomUser actor) => !Grid.Layered
        || RestingStack(roller).Levels.Any(level => Math.Abs(actor.Movement.SupportZ - level) <= SurfaceSelection.Tolerance);

    public bool RestsOnRoller(Item roller, Item cargo) => !Grid.Layered || RestingStack(roller).Items.Contains(cargo.Id);

    // The physical stack a layered roller carries: items resting at the roller's top or on an item that does,
    // and the levels an actor may stand on (the roller top and each stacked item's top).
    private (HashSet<uint> Items, List<double> Levels) RestingStack(Item roller)
    {
        var items = new HashSet<uint>();
        var levels = new List<double> { roller.TotalHeight };
        var candidates = context.Room.GetGameMap().GetRoomItemForSquare(roller.GetX, roller.GetY, roller.GetZ);
        bool grew;

        do
        {
            grew = false;

            foreach (var item in candidates)
            {
                if (!items.Contains(item.Id) && levels.Any(level => Math.Abs(item.GetZ - level) <= SurfaceSelection.Tolerance))
                {
                    items.Add(item.Id);
                    levels.Add(item.TotalHeight);
                    grew = true;
                }
            }
        } while (grew);

        return (items, levels);
    }

    public void RefreshCapabilities(IEnumerable<RoomUser> actors)
    {
        foreach (var actor in actors)
        {
            context.Profiles.Refresh(actor);
        }
    }

    public bool AdmitsActor(RollerMove move, RollerDepartures departing)
    {
        var to = move.Destination;

        if (!Grid.InBounds(to.X, to.Y))
        {
            return false;
        }

        var actor = move.Actor!;
        var slot = LandingSlot(to, move.CarriedZ);
        var profile = actor.Movement.Profile;
        var from = new NavPosition(move.Origin.X, move.Origin.Y, move.SourceZ);
        var occupancy = context.OccupancyAt(actor, slot, departing.Users);
        var rules = new MovementRules(Grid, navigation.Settings);
        var result = departing.IsEmpty || departing.Roller?.Definition.Walkable != true
            ? rules.CanStep(profile, from, Grid.Position(slot), StepPurpose.Roller, OccupancyView.Execution, occupancy)
            : rules.CanRollOntoVacatedRoller(profile, from, Grid.Position(slot), occupancy,
                departing.ReleasesFloorStatus(context.Room));

        return result.Ok;
    }

    // Furniture may not land where another actor stands, walks or holds any claim, on any surface of any footprint tile.
    public bool AdmitsCargo(RollerMove move, IRollerDepartureView departures)
        => CargoFootprint(move).All(tile => !Grid.InBounds(tile.X, tile.Y) || TileSlots(tile).All(slot
            => (context.Claims.OccupancyAt(slot, ClaimLedger.NoGroup, departures.At(tile).Users) & Anything) == 0));

    // One R claim per tile the group lands on, with that tile's own departures excluded; all or nothing.
    // Layered rooms also reserve a cargo footprint tile an actor of the same group lands on: the actor's
    // claim covers one surface, the cargo excludes the whole tile.
    public bool Reserve(TransportGroup group)
    {
        var reserved = new List<Action>();

        foreach (var (tile, landing) in LandingTiles(group))
        {
            var release = TryReserve(tile, landing, group.At(tile), Riders(group));

            if (release != null)
            {
                reserved.Add(release);
                continue;
            }

            foreach (var undo in reserved)
            {
                undo();
            }

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
            actor.IsRolling = true;
            actor.RollerDelay = 1;
        }
    }

    public void Publish(IReadOnlyList<RollerMove> moves)
    {
        navigation.ApplyDirty();

        foreach (var move in moves)
        {
            placement.Bind(move.Actor!, move.CarriedZ, ForceResolution.ExactZ);
            context.RefreshMembership(move.Actor!);

            if (Grid.Layered)
            {
                RebindRollerClaim(move);
            }
        }
    }

    // The group's own cargo may replace the reserved landing surface; the R claim is held through the
    // user phase (§14.9), so it moves to the surface at the final carried Z before any hook runs.
    private void RebindRollerClaim(RollerMove move)
    {
        context.Claims.ReleaseRollers(move.Actor!);
        context.Claims.EnsureCapacity(Grid.SlotCapacity);
        context.Claims.TryClaim(move.Actor!, LandingSlot(move.Destination, move.CarriedZ), ClaimKind.Roller, TargetOccupancy.None);
    }

    public void Land(RollerMove move) => context.TransportLandings.Trigger(move.Actor!, move.Destination, move.Roller);

    // Actor destinations first, then every cargo's whole destination footprint. With K=1 an arriving
    // actor's claim already covers its tile; layered rooms reserve that tile for the cargo as well.
    private List<(Point Tile, RollerMove? Landing)> LandingTiles(TransportGroup group)
    {
        var tiles = group.Moves.Where(move => move.Actor != null).GroupBy(move => move.Destination)
            .Select(landing => (landing.Key, (RollerMove?)landing.First())).ToList();
        var cargo = group.Moves.Where(move => move.Cargo != null).SelectMany(CargoFootprint).Distinct()
            .Where(tile => Grid.Layered || tiles.All(landing => landing.Key != tile));
        tiles.AddRange(cargo.Select(tile => (tile, (RollerMove?)null)));

        return tiles;
    }

    private static HashSet<RoomUser> Riders(TransportGroup group)
        => group.Moves.Where(move => move.Actor != null).Select(move => move.Actor!).ToHashSet<RoomUser>(ReferenceEqualityComparer.Instance);

    private static IEnumerable<Point> CargoFootprint(RollerMove move)
        => WiredRoomOperations.Footprint(move.Cargo!, move.Destination.X, move.Destination.Y, move.Cargo!.Rotation);

    private Action? TryReserve(Point tile, RollerMove? landing, RollerDepartures departing, IReadOnlySet<RoomUser> riders)
    {
        if (!Grid.InBounds(tile.X, tile.Y))
        {
            return () => { };
        }

        if (landing?.Actor is not { } actor)
        {
            return TryReserveCargo(tile, departing, riders);
        }

        var slot = LandingSlot(tile, landing.CarriedZ);
        var mask = ClaimMatrix.BlockingMask(actor.Movement.Profile, Grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);

        return context.Claims.TryClaim(actor, slot, ClaimKind.Roller, mask, departing.Users)
            ? () => context.Claims.ReleaseRollers(actor) : null;
    }

    // Cargo reserves the whole tile it lands on; the group's own riders never block it.
    private Action? TryReserveCargo(Point tile, RollerDepartures departing, IReadOnlySet<RoomUser> riders)
    {
        var index = Grid.Tile(tile.X, tile.Y);
        var excluded = Grid.Layered ? departing.Users.Union(riders).ToHashSet<RoomUser>(ReferenceEqualityComparer.Instance) : departing.Users;

        return context.Claims.TryReserveCargo(index, Anything, excluded) ? () => context.Claims.ReleaseCargo(index) : null;
    }

    // The carried actor lands on the surface at exactly its carried Z, else it is checked against the tile's top.
    private int LandingSlot(Point tile, double carriedZ)
    {
        var index = Grid.Tile(tile.X, tile.Y);
        var exact = SurfaceSelection.Select(Grid, index, carriedZ, ForceResolution.ExactZ);

        return exact >= 0 ? exact : Grid.TopSlot(index);
    }

    // The tile's own slot plus every other compiled surface on it (only the own slot with K = 1).
    private IEnumerable<int> TileSlots(Point tile)
    {
        var index = Grid.Tile(tile.X, tile.Y);
        yield return index;

        for (var ordinal = 0; ordinal < Grid.SurfaceCount(index); ordinal++)
        {
            if (Grid.SurfaceAt(index, ordinal) != index)
            {
                yield return Grid.SurfaceAt(index, ordinal);
            }
        }
    }
}
