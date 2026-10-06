using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

// One standable surface candidate. Role orders coincident candidates: seat/bed, guild gate, walkable top, floor.
internal readonly record struct SurfaceCandidate(double Z, NavFlags Flags, SurfaceKind Kind, uint Support, int Group, int Role,
    NavItemRecord? Item = null)
{
    public bool Standable => Flags != NavFlags.None;
}

// Effective-kind rules shared by the compatibility and layered compilers (§5.4).
internal static class SurfaceRules
{
    internal const double Epsilon = 0.001;

    internal static SurfaceCandidate ItemCandidate(NavItemRecord item)
    {
        if (item.Seat || item.Interaction is InteractionType.Bed or InteractionType.TentSmall) {
            return item.Seat
                ? new(item.Z, NavFlags.GoalOnlySeat, SurfaceKind.SeatBase, item.ItemId, 0, 3, item)
                : new(item.Z, NavFlags.GoalOnlyBed, SurfaceKind.BedBase, item.ItemId, 0, 3, item);
        }

        if (item.Interaction == InteractionType.GuildGate) {
            return new(item.Top, NavFlags.Transit | NavFlags.GuildGate, SurfaceKind.GateBase, item.ItemId, item.GroupId, 2, item);
        }

        var flags = item.Walkable || OpenGate(item) ? NavFlags.Transit : NavFlags.None;

        if (item.Interaction == InteractionType.Roller && flags != NavFlags.None) {
            flags |= NavFlags.Roller;
        }

        return new(item.Top, flags, SurfaceKind.Top, item.ItemId, 0, 1, item);
    }

    internal static SurfaceCandidate? FloorCandidate(SquareState state, double z) => state switch
    {
        SquareState.Open => new SurfaceCandidate(z, NavFlags.Transit, SurfaceKind.Floor, 0, 0, 0),
        SquareState.Seat => new SurfaceCandidate(z, NavFlags.GoalOnlySeat | NavFlags.ModelSeat, SurfaceKind.Floor, 0, 0, 0),
        _ => null
    };

    // Walkable items, open gates, seats and beds block [z, z + h); everything else, including a closed
    // non-walkable guild gate, blocks at least epsilon, so zero-height blockers still block.
    internal static (double From, double To) BlockingInterval(NavItemRecord item)
    {
        var passable = item.Walkable || item.Seat || OpenGate(item)
            || item.Interaction is InteractionType.Bed or InteractionType.TentSmall;

        return (item.Z, item.Z + (passable ? item.Height : Math.Max(item.Height, Epsilon)));
    }

    internal static int[] PillowRow(NavItemRecord bed, int width)
    {
        // Pillow row is the item's leading footprint row.
        var across = bed.Rotation is 2 or 6;
        var row = across ? bed.Footprint.Min(p => p % width) : bed.Footprint.Min(p => p / width);

        return bed.Footprint.Where(p => (across ? p % width : p / width) == row).ToArray();
    }

    private static bool OpenGate(NavItemRecord item) => item.Interaction == InteractionType.Gate && item.State == "1";
}
