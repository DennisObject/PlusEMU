namespace Plus.HabboHotel.Rooms.PathFinding;

// Turns one cycle's loads into independent transport groups.
internal sealed class RollerTransportPlanner(TransportFeasibilityResolver resolver, TransportGroupBuilder builder)
{
    internal IReadOnlyList<TransportGroup> Plan(IReadOnlyList<RollerLoad> loads)
        => builder.Build(resolver.Resolve(new RollerGraph(loads)));
}
