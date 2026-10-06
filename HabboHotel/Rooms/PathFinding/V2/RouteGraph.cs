namespace Plus.HabboHotel.Rooms.PathFinding;

public enum GraphView : byte
{
    Surface, LegacyTile
}
internal readonly record struct LandingSurface(NavPosition Position, SurfaceRef? Support);

// Route identity belongs to its search view; physical support belongs to current geometry.
// The LegacyTile view addresses tiles; the Surface view addresses surface slots (several per tile when layered).
internal sealed class RouteGraph(NavGrid grid)
{
    internal int Slot(SurfaceRef step, GraphView view) => view == GraphView.LegacyTile
        ? (uint)step.Tile < grid.TileCount ? step.Tile : -1
        : grid.SlotOf(step);
    internal bool IsValid(SurfaceRef step, GraphView view) => Slot(step, view) >= 0;
    internal NavPosition Position(SurfaceRef step, GraphView view)
    {
        var slot = Slot(step, view);

        return grid.Position(slot < 0 ? step.Tile : slot, view == GraphView.LegacyTile);
    }
    internal bool IsSeat(SurfaceRef step)
    {
        var slot = grid.Layered ? grid.SlotOf(step) : step.Tile;

        return slot >= 0 && grid.Kind[slot] == SurfaceKind.SeatBase;
    }
    internal double RiderOffset(RoomUser actor, int slot)
        => PostureService.RiderOffset(actor, grid.Reference(slot));
    internal LandingSurface ResolveLanding(SurfaceRef step, GraphView view, double plannedZ)
    {
        if (grid.Layered) {
            return ResolveLayeredLanding(step, view, plannedZ);
        }

        var tile = step.Tile;
        var position = grid.Position(tile, view == GraphView.LegacyTile);

        if (view == GraphView.Surface && !grid.Active(tile)) {
            position = position with { Z = plannedZ };
        }

        var support = grid.Active(tile) && Math.Abs(grid.WalkZ[tile] - position.Z) <= .001
            ? grid.Reference(tile) : (SurfaceRef?)null;

        return new(position, support);
    }

    // A legacy-view step supports on the surface at its exact Z. A surface step keeps its own surface;
    // if a just-applied rebuild removed it, it lands as a rebind would: highest at or below, else highest.
    private LandingSurface ResolveLayeredLanding(SurfaceRef step, GraphView view, double plannedZ)
    {
        if (view == GraphView.LegacyTile) {
            var position = grid.Position(step.Tile, true);
            var exact = SurfaceSelection.Select(grid, step.Tile, position.Z, ForceResolution.ExactZ);

            return exact < 0 ? new(position, null) : new(position with { Slot = exact }, grid.Reference(exact));
        }

        var slot = grid.SlotOf(step);

        if (slot < 0 || !grid.Active(slot)) {
            slot = SurfaceSelection.Select(grid, step.Tile, plannedZ, ForceResolution.NearestAtOrBelow);
        }

        if (slot < 0) {
            slot = SurfaceSelection.Select(grid, step.Tile, plannedZ, ForceResolution.Highest);
        }

        return slot < 0 ? new(grid.Position(step.Tile) with { Z = plannedZ }, null) : new(grid.Position(slot), grid.Reference(slot));
    }
}
