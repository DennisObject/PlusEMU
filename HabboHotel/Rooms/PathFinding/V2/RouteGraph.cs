namespace Plus.HabboHotel.Rooms.PathFinding;

public enum GraphView : byte { Surface, LegacyTile }
internal readonly record struct LandingSurface(NavPosition Position, SurfaceRef? Support);

// Route identity belongs to its search view; physical support belongs to current geometry.
internal sealed class RouteGraph(NavGrid grid)
{
    internal bool IsValid(SurfaceRef step, GraphView view) => (uint)step.Tile < grid.SlotCapacity
        && (view == GraphView.LegacyTile || grid.Reference(step.Tile) == step);
    internal NavPosition Position(int tile, GraphView view)
        => grid.Position(tile, view == GraphView.LegacyTile);
    internal bool IsSeat(int tile) => grid.Kind[tile] == SurfaceKind.SeatBase;
    internal double RiderOffset(RoomUser actor, int tile)
        => PostureService.RiderOffset(actor, grid.Reference(tile));
    internal LandingSurface ResolveLanding(int tile, GraphView view, double previousZ)
    {
        var position = Position(tile, view);
        if (view == GraphView.Surface && !grid.Active(tile)) position = position with { Z = previousZ };
        var support = grid.Active(tile) && Math.Abs(grid.WalkZ[tile] - position.Z) <= .001
            ? grid.Reference(tile) : (SurfaceRef?)null;
        return new(position, support);
    }
}
