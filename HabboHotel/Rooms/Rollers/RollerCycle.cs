using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Rollers;

// One roller cycle for either engine: snapshot every load, plan transport groups, commit each group.
internal sealed class RollerCycle
{
    private readonly RollerLoadBuilder _loads;
    private readonly RollerTransportPlanner _planner;
    private readonly TransportGroupCommitter _committer;

    internal RollerCycle(Room room, IRollerTransportEngine engine)
    {
        var admission = new RollerAdmission(room, engine);
        _loads = new(room, engine);
        _planner = new(new(admission), new());
        _committer = new(room, engine, new(room, engine, admission));
    }

    internal void Run(IEnumerable<Item> rollers)
    {
        foreach (var group in _planner.Plan(_loads.Build(rollers))) _committer.TryCommit(group);
    }
}
