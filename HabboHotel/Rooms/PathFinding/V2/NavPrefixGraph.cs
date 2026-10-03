namespace Plus.HabboHotel.Rooms.PathFinding;

// PrefixView over compiled surfaces: full CanStep with an occupancy holding only stationary and
// off-graph members, so claims and walking members never shorten a prefix. Actor and purpose
// exemptions still come from the claim matrix.
internal sealed class NavPrefixGraph(NavGrid grid, MovementRules rules, ActorProfile profile,
    PlanningOccupancy occupancy) : IPrefixGraph
{
    public const TargetOccupancy Kept = TargetOccupancy.Stationary | TargetOccupancy.OffGraph;

    // Compatibility mode compiles one surface per tile (K = 1).
    public int Candidates(int x, int y, Span<PrefixCandidate> into)
    {
        if (!grid.InBounds(x, y)) return 0;
        var slot = grid.Tile(x, y);
        if (!grid.Active(slot) && !profile.LegacyOverride) return 0;
        var z = profile.LegacyOverride ? grid.LegacyZ[slot] : grid.WalkZ[slot];
        into[0] = new(x, y, z, grid.SupportItem[slot], slot);
        return 1;
    }

    public bool CanStep(in PrefixCandidate from, in PrefixCandidate to, StepPurpose purpose)
        => rules.CanStep(profile, new(from.X, from.Y, from.Z, from.Slot), new(to.X, to.Y, to.Z, to.Slot),
            purpose, OccupancyView.Execution, occupancy).Ok;
}
