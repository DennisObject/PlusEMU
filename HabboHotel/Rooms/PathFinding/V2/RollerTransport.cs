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
        || SurfaceContacts.ContactSlot(Grid, actor.X, actor.Y, actor.Movement.CurrentRef, actor.Movement.SupportZ) == RollerSlot(roller);

    public bool RestsOnRoller(Item roller, Item cargo) => !Grid.Layered
        || SurfaceContacts.Owner(Grid, Grid.Tile(roller.GetX, roller.GetY), cargo) == RollerSlot(roller);

    // The surface the roller provides: the one that owns the roller item.
    private int RollerSlot(Item roller)
    {
        if (!Grid.InBounds(roller.GetX, roller.GetY)) return -1;
        return SurfaceContacts.Owner(Grid, Grid.Tile(roller.GetX, roller.GetY), roller);
    }

    public void RefreshCapabilities(IEnumerable<RoomUser> actors)
    {
        foreach (var actor in actors) context.Profiles.Refresh(actor);
    }

    public bool AdmitsActor(RollerMove move, RollerDepartures departing)
    {
        var to = move.Destination;
        if (!Grid.InBounds(to.X, to.Y)) return false;
        var actor = move.Actor!; var slot = LandingSlot(to, move.CarriedZ);
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
    public bool Reserve(TransportGroup group)
    {
        var reserved = new List<Action>();
        foreach (var (tile, landing) in LandingTiles(group))
        {
            var release = TryReserve(tile, landing, group.At(tile));
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
    private static Dictionary<Point, RollerMove?> LandingTiles(TransportGroup group)
    {
        var tiles = new Dictionary<Point, RollerMove?>();
        foreach (var move in group.Moves.Where(move => move.Actor != null)) tiles.TryAdd(move.Destination, move);
        foreach (var tile in group.Moves.Where(move => move.Cargo != null).SelectMany(CargoFootprint)) tiles.TryAdd(tile, null);
        return tiles;
    }

    private static IEnumerable<Point> CargoFootprint(RollerMove move)
        => WiredRoomOperations.Footprint(move.Cargo!, move.Destination.X, move.Destination.Y, move.Cargo!.Rotation);

    private Action? TryReserve(Point tile, RollerMove? landing, RollerDepartures departing)
    {
        if (!Grid.InBounds(tile.X, tile.Y)) return () => { };
        if (landing?.Actor is not { } actor) return TryReserveCargo(tile, departing);
        var slot = LandingSlot(tile, landing.CarriedZ);
        var mask = ClaimMatrix.BlockingMask(actor.Movement.Profile, Grid.Flags[slot], StepPurpose.Roller, OccupancyView.Execution);
        return context.Claims.TryClaim(actor, slot, ClaimKind.Roller, mask, departing.Users)
            ? () => context.Claims.ReleaseRollers(actor) : null;
    }

    // Cargo reserves every surface of the tile it lands on; all or nothing.
    private Action? TryReserveCargo(Point tile, RollerDepartures departing)
    {
        var reserved = new List<int>();
        foreach (var slot in TileSlots(tile))
        {
            if (context.Claims.TryReserveCargo(slot, Anything, departing.Users)) { reserved.Add(slot); continue; }
            foreach (var undo in reserved) context.Claims.ReleaseCargo(undo);
            return null;
        }
        return () => { foreach (var slot in reserved) context.Claims.ReleaseCargo(slot); };
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
            if (Grid.SurfaceAt(index, ordinal) != index) yield return Grid.SurfaceAt(index, ordinal);
    }
}
