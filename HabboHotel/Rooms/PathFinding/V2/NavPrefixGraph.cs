namespace Plus.HabboHotel.Rooms.PathFinding;

// PrefixView over compiled surfaces: full CanStep with an occupancy holding only stationary and
// off-graph members, so claims and walking members never shorten a prefix. Actor and purpose
// exemptions still come from the claim matrix.
internal sealed class NavPrefixGraph(NavGrid grid, MovementRules rules, ActorProfile profile,
    PlanningOccupancy occupancy) : IPrefixGraph
{
    public const TargetOccupancy Kept = TargetOccupancy.Stationary | TargetOccupancy.OffGraph;

    // Every compiled surface on the tile (one with K = 1); the LegacyTile view has one node per tile.
    public int Candidates(int x, int y, Span<PrefixCandidate> into)
    {
        if (!grid.InBounds(x, y)) {
            return 0;
        }

        var tile = grid.Tile(x, y);

        if (profile.LegacyOverride) {
            into[0] = new(x, y, grid.LegacyZ[tile], grid.SupportItem[tile], tile);

            return 1;
        }

        var count = 0;

        for (var ordinal = 0; ordinal < grid.SurfaceCount(tile); ordinal++) {
            var slot = grid.SurfaceAt(tile, ordinal);

            if (grid.Active(slot)) {
                into[count++] = new(x, y, grid.WalkZ[slot], grid.SupportItem[slot], slot);
            }
        }

        return count;
    }

    public bool CanStep(in PrefixCandidate from, in PrefixCandidate to, StepPurpose purpose)
        => rules.CanStep(profile, new(from.X, from.Y, from.Z, from.Slot), new(to.X, to.Y, to.Z, to.Slot),
            purpose, OccupancyView.Execution, occupancy).Ok;
}
