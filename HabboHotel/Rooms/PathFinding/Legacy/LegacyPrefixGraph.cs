namespace Plus.HabboHotel.Rooms.PathFinding;

// Legacy has one surface per tile; steps use the pure check under the prefix view.
internal sealed class LegacyPrefixGraph(Gamemap map, RoomUser user) : IPrefixGraph
{
    public int Candidates(int x, int y, Span<PrefixCandidate> into)
    {
        if (!map.ValidTile(x, y)) return 0;
        into[0] = new(x, y, map.SqAbsoluteHeight(x, y), 0, y * map.Model.MapSizeX + x);
        return 1;
    }

    public bool CanStep(in PrefixCandidate from, in PrefixCandidate to, StepPurpose purpose)
        => map.IsValidStepPure(user, new(from.X, from.Y), new(to.X, to.Y), purpose == StepPurpose.Goal,
            user.AllowOverride, LegacyStepView.Prefix).Ok;
}
