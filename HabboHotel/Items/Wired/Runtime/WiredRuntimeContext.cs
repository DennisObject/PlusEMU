using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;

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
    internal bool ResumeImmediately { get; set; }
    internal WiredFurniturePublication? Publication { get; set; }
    internal WiredFurniturePublication? InheritedPublication { get; set; }
    public long NowMilliseconds { get; internal set; }
    public WiredSelection Triggering { get; internal set; } = new();
    public WiredSelection SelectorPool { get; } = new();
    internal List<uint> SelectorFurniOrder { get; } = [];
    internal List<int> SelectorUserOrder { get; } = [];
    public WiredSelection Selected { get; internal set; } = new();
    public WiredSelectionKind SelectorKinds { get; internal set; }
    public WiredSignalPayload? Signal { get; internal set; }
    public Dictionary<string, long> Values { get; } = [];
    public WiredSelectorWorld? SelectorWorldSnapshot { get; set; }
    public WiredVariableFrame? VariableFrame { get; set; }
    public WiredVariableChangeBatch? VariableChanges { get; set; }
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

        foreach (var item in targets.AttachedFurni()) {
            FurniIdentity[item.Id] = item;
        }

        foreach (var user in targets.AllUsers()) {
            UserIdentity[user.VirtualId] = user;
        }

        if (@event.Actor != null) {
            UserIdentity[@event.Actor.VirtualId] = @event.Actor;
        }

        if (@event.TargetUser != null) {
            UserIdentity[@event.TargetUser.VirtualId] = @event.TargetUser;
        }

        if (@event.EventItem != null) {
            FurniIdentity[@event.EventItem.Id] = @event.EventItem;
        }
    }

    private WiredRuntimeContext(WiredRuntimeContext parent, WiredRuntimeEvent @event, bool shareFiring = false)
    {
        Room = parent.Room;
        Event = @event;
        Targets = parent.Targets;
        Operations = parent.Operations;
        FurniIdentity = parent.FurniIdentity;
        UserIdentity = parent.UserIdentity;

        if (shareFiring) {
            _configurations = parent._configurations;
            Policy = parent.Policy;
            SelectorPool = parent.SelectorPool;
            SelectorFurniOrder = parent.SelectorFurniOrder;
            SelectorUserOrder = parent.SelectorUserOrder;
            Values = parent.Values;
        }
    }

    // Executors read one immutable snapshot throughout a firing, including delayed actions.
    public WiredConfiguration ConfigurationOf(IWiredConfiguredItem box) =>
        _configurations.TryGetValue(box.Item.Id, out var captured) ? captured : box.Configuration;

    internal void Capture(IEnumerable<IWiredItem> stack)
    {
        foreach (var box in stack.OfType<IWiredConfiguredItem>()) {
            _configurations[box.Item.Id] = box.Configuration;
        }
    }

    internal WiredRuntimeContext ForActor(RoomUser actor)
    {
        var context = new WiredRuntimeContext(this, Event with { Actor = actor }, shareFiring: true)
        {
            Depth = Depth,
            ResumeImmediately = ResumeImmediately,
            Publication = Publication,
            InheritedPublication = InheritedPublication,
            NowMilliseconds = NowMilliseconds,
            Trigger = Trigger,
            Triggering = Triggering.Copy(),
            Selected = Selected.Copy(),
            SelectorKinds = SelectorKinds,
            Signal = Signal,
            VariableFrame = VariableFrame,
            SelectorWorldSnapshot = SelectorWorldSnapshot
        };
        context.Triggering.UserIds.Clear();
        context.Triggering.UserIds.Add(actor.VirtualId);

        if (!SelectorKinds.HasFlag(WiredSelectionKind.Users)) {
            context.Selected.UserIds.Clear();
            context.Selected.UserIds.Add(actor.VirtualId);
        }

        return context;
    }

    internal WiredRuntimeContext Fork(WiredRuntimeEvent @event, int depth)
    {
        // One identity snapshot per dispatch; children share it and revalidate only targets.
        var child = new WiredRuntimeContext(this, @event) { Depth = depth, NowMilliseconds = NowMilliseconds };
        child.VariableFrame = VariableFrame;
        child.InheritedPublication = Publication ?? InheritedPublication;

        foreach (var pair in Values) {
            child.Values[pair.Key] = pair.Value;
        }

        return child;
    }
}
