namespace Plus.HabboHotel.Rooms.PathFinding;

// Every eligible surface of the goal tile, in surface order (at most four). Slot is the first one, or -1.
// Identity is shared by every search precheck.
public readonly record struct AcceptedGoal(int X, int Y, int Slot, int Slot1 = -1, int Slot2 = -1, int Slot3 = -1)
{
    public int Count => Slot < 0 ? 0 : Slot1 < 0 ? 1 : Slot2 < 0 ? 2 : Slot3 < 0 ? 3 : 4;
    public int this[int index] => index switch { 0 => Slot, 1 => Slot1, 2 => Slot2, _ => Slot3 };
    public bool Contains(int slot) => slot >= 0 && (slot == Slot || slot == Slot1 || slot == Slot2 || slot == Slot3);
    internal AcceptedGoal With(int slot) => Count switch
    {
        0 => this with { Slot = slot },
        1 => this with { Slot1 = slot },
        2 => this with { Slot2 = slot },
        _ => this with { Slot3 = slot }
    };
}

public static class GoalResolver
{
    // Click resolution is separate from Find: shadow searches must use legacy GoalX/Y.
    // P2 calls this at command consumption, then passes the resolved accepted identity.
    public static AcceptedGoal ResolveClick(NavGrid grid, ActorProfile actor, in NavPosition start,
        int x, int y, PlanningOccupancy? occupancy, ActorAccessResolver? access = null)
    {
        if (!grid.InBounds(x, y))
        {
            return new(x, y, -1);
        }

        var bed = BedSlot(grid, grid.Tile(x, y));

        if (bed < 0 || actor.LegacyOverride)
        {
            return Resolve(grid, actor, x, y, occupancy, access);
        }

        var best = new AcceptedGoal(x, y, -1);
        var distance = int.MaxValue;

        foreach (var tile in grid.PillowTiles[bed])
        {
            var pillow = PillowSlot(grid, tile, grid.SupportItem[bed]);

            if (pillow < 0 || !Accepts(grid, actor, pillow, occupancy, access))
            {
                continue;
            }

            var nextDistance = PathTieBreak.Heuristic(start.X, start.Y, tile % grid.Width, tile / grid.Width);

            if (nextDistance < distance)
            {
                best = new(tile % grid.Width, tile / grid.Width, pillow);
                distance = nextDistance;
            }
        }

        return best;
    }

    public static AcceptedGoal Resolve(NavGrid grid, ActorProfile actor, int x, int y, PlanningOccupancy? occupancy,
        ActorAccessResolver? access = null)
    {
        if (!grid.InBounds(x, y))
        {
            return new(x, y, -1);
        }

        var t = grid.Tile(x, y);

        if (actor.LegacyOverride)
        {
            return new(x, y, t);
        }

        var goal = new AcceptedGoal(x, y, -1);

        for (var ordinal = 0; ordinal < grid.SurfaceCount(t); ordinal++)
        {
            var slot = grid.SurfaceAt(t, ordinal);

            if (Accepts(grid, actor, slot, occupancy, access))
            {
                goal = goal.With(slot);
            }
        }

        return goal;
    }

    private static bool Accepts(NavGrid grid, ActorProfile actor, int slot, PlanningOccupancy? occupancy, ActorAccessResolver? access)
    {
        var flags = grid.Flags[slot];

        return (flags & (NavFlags.Transit | NavFlags.GoalOnlySeat | NavFlags.GoalOnlyBed | NavFlags.Door)) != 0
            && (flags & NavFlags.FloorLocked) == 0
            && ((flags & NavFlags.GuildGate) == 0 || (access ?? ActorAccessResolver.Cached).CanEnterGuildGate(actor, grid.GroupId[slot]))
            && (occupancy == null || (occupancy.Targets[slot] & ClaimMatrix.BlockingMask(actor, flags, StepPurpose.Goal, OccupancyView.Execution)) == 0);
    }

    private static int BedSlot(NavGrid grid, int tile)
    {
        for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++)
        {
            var slot = grid.SurfaceAt(tile, ordinal);

            if ((grid.Flags[slot] & NavFlags.GoalOnlyBed) != 0)
            {
                return slot;
            }
        }

        return -1;
    }

    private static int PillowSlot(NavGrid grid, int tile, uint bed)
    {
        for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++)
        {
            var slot = grid.SurfaceAt(tile, ordinal);

            if (grid.SupportItem[slot] == bed && (grid.Flags[slot] & NavFlags.GoalOnlyBed) != 0)
            {
                return slot;
            }
        }

        return -1;
    }
}
