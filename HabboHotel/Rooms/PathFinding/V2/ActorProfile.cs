namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class ActorProfile
{
    public bool LegacyOverride { get; set; }
    public bool IgnoreStepHeight { get; set; }
    public bool IgnoreUsers { get; set; }
    // Plus's inverted RoomBlockingEnabled: true means walkthrough.
    public bool Walkthrough { get; set; }
    public bool DiagonalEnabled { get; set; } = true;
    public int CapabilityVersion { get; private set; }
    private readonly HashSet<int> _groups = new();
    public bool IsMember(int group) => _groups.Contains(group);
    public void SetMembership(int group, bool member)
    {
        if (member ? _groups.Add(group) : _groups.Remove(group)) CapabilityVersion++;
    }
    public InteractionAuthorization? Interaction { get; set; }
}

public readonly record struct InteractionAuthorization(int FromX, int FromY, int ToX, int ToY)
{
    public bool Allows(in NavPosition from, in NavPosition to) => from.X == FromX && from.Y == FromY && to.X == ToX && to.Y == ToY;
}
