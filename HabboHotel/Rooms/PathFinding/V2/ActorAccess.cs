namespace Plus.HabboHotel.Rooms.PathFinding;

// Live guild membership cached on the actor. CapabilityVersion bumps when a group is joined or left.
public sealed class ActorAccess
{
    private readonly HashSet<int> _groups = new();
    public int CapabilityVersion { get; private set; }
    public IReadOnlyCollection<int> GroupIds => _groups;
    public bool IsMember(int groupId) => _groups.Contains(groupId);

    internal void SetMembership(int groupId, bool member)
    {
        if (member ? _groups.Add(groupId) : _groups.Remove(groupId)) CapabilityVersion++;
    }
}
