using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public sealed class WiredRuntimeContext
{
    private readonly Dictionary<uint, WiredConfiguration> _configurations = [];
    internal Dictionary<uint, Item> FurniIdentity { get; } = [];
    internal Dictionary<int, RoomUser> UserIdentity { get; } = [];
    public Room Room { get; }
    public WiredRuntimeEvent Event { get; }
    public IWiredItem? Trigger { get; internal set; }
    public int Depth { get; internal set; }
    public long NowMilliseconds { get; internal set; }
    public WiredSelection Triggering { get; internal set; } = new();
    public WiredSelection SelectorPool { get; } = new();
    public WiredSelection Selected { get; internal set; } = new();
    public WiredSelectionKind SelectorKinds { get; internal set; }
    public WiredSignalPayload? Signal { get; internal set; }
    public Dictionary<string, long> Values { get; } = [];
    public WiredExecutionPolicy Policy { get; } = new();
    public WiredTargetResolver Targets { get; }
    public IWiredRuntimeOperations Operations { get; }

    public WiredRuntimeContext(Room room, WiredRuntimeEvent @event, WiredTargetResolver targets,
        IWiredRuntimeOperations operations)
    {
        Room = room;
        Event = @event;
        Targets = targets;
        Operations = operations;
        foreach (var item in targets.AllFurni()) FurniIdentity[item.Id] = item;
        foreach (var user in targets.AllUsers()) UserIdentity[user.VirtualId] = user;
        if (@event.Actor != null) UserIdentity[@event.Actor.VirtualId] = @event.Actor;
        if (@event.TargetUser != null) UserIdentity[@event.TargetUser.VirtualId] = @event.TargetUser;
        if (@event.EventItem != null) FurniIdentity[@event.EventItem.Id] = @event.EventItem;
    }

    // Executors read one immutable snapshot throughout a firing, including delayed actions.
    public WiredConfiguration ConfigurationOf(IWiredConfiguredItem box) =>
        _configurations.TryGetValue(box.Item.Id, out var captured) ? captured : box.Configuration;

    internal void Capture(IEnumerable<IWiredItem> stack)
    {
        foreach (var box in stack.OfType<IWiredConfiguredItem>()) _configurations[box.Item.Id] = box.Configuration;
    }

    internal WiredRuntimeContext Fork(WiredRuntimeEvent @event, int depth)
    {
        var child = new WiredRuntimeContext(Room, @event, Targets, Operations) { Depth = depth, NowMilliseconds = NowMilliseconds };
        // Preserve sender identities even if a room VirtualId has been reused before receipt.
        foreach (var pair in FurniIdentity) child.FurniIdentity[pair.Key] = pair.Value;
        foreach (var pair in UserIdentity) child.UserIdentity[pair.Key] = pair.Value;
        foreach (var pair in Values) child.Values[pair.Key] = pair.Value;
        return child;
    }
}
