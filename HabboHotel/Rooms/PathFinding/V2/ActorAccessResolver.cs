using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.PathFinding;

public enum GroupStanding { Unresolved, Outsider, Member }

// The one owner of actor access. Plus has a single rule today (guild gates); future rules plug in here.
public class ActorAccessResolver(Func<int, int, GroupStanding> source)
{
    public static ActorAccessResolver Cached { get; } = new((_, _) => GroupStanding.Unresolved);

    // Same lookup the legacy guild-gate validation has always made, bound to the live game at call time.
    public static ActorAccessResolver Live { get; } = new((groupId, habboId) => LookUp(PlusEnvironment.Game, groupId, habboId));

    public ActorAccessResolver(Func<int, int, bool> isMember)
        : this((groupId, habboId) => isMember(groupId, habboId) ? GroupStanding.Member : GroupStanding.Outsider) { }

    public static ActorAccessResolver ForGame(IGame game) => new((groupId, habboId) => LookUp(game, groupId, habboId));

    // Check 6 of CanStep, CanFlank and goal acceptance read the cached set refreshed by `Refresh`.
    public virtual bool CanEnterGuildGate(ActorProfile actor, int groupId) => actor.Access.IsMember(groupId);

    public GroupStanding Standing(int? habboId, int groupId)
        => habboId is { } id ? source(groupId, id) : GroupStanding.Unresolved;

    public GroupStanding StandingOf(RoomUser actor, int groupId) => Standing(HabboIdOf(actor), groupId);

    public void Refresh(ActorProfile profile, int? habboId, IEnumerable<int> groupIds)
    {
        foreach (var groupId in groupIds)
            profile.Access.SetMembership(groupId, Standing(habboId, groupId) == GroupStanding.Member);
    }

    private static int? HabboIdOf(RoomUser actor) => actor.GetClient()?.GetHabbo()?.Id;

    private static GroupStanding LookUp(IGame game, int groupId, int habboId)
        => !game.GroupManager.TryGetGroup(groupId, out var group) ? GroupStanding.Unresolved
            : group.IsMember(habboId) ? GroupStanding.Member : GroupStanding.Outsider;
}
