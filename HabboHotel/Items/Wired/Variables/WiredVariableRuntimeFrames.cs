using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Rebuilds live holders through the engine's captured-object identity guards; shares only execution context values.</summary>
public static class WiredVariableRuntimeFrames
{
    internal static WiredVariableFrame CaptureInspection(WiredRuntimeContext context) => new(context.Room.Id,
        context.UserIdentity.Values.Where(user => !user.IsBot).Select(UserHolder)
            .Concat(context.FurniIdentity.Values.Select(FurniHolder)).ToArray())
    { RuntimeContext = context };

    public static WiredVariableFrame Create(WiredRuntimeContext context, WiredVariableFrame? parent = null)
    {
        IEnumerable<WiredVariableHolder> Furni(IEnumerable<Item> items) => items.Select(FurniHolder);
        IEnumerable<WiredVariableHolder> Users(IEnumerable<RoomUser> users) => users.Select(UserHolder);
        // AllRoom is deliberately floor-only. Captured trigger/signal furniture can also be wall items.
        // Resolve both through the same live identity guards, without applying operand quantity caps.
        var holders = Furni(context.Targets.ResolveFurni(context, [], WiredSources.AllRoom, raw: true))
            .Concat(Furni(context.Targets.ResolveFurni(context, [], WiredSources.Trigger, raw: true)))
            .Concat(Furni(context.Targets.ResolveFurni(context, [], WiredSources.Signal, raw: true)))
            .Concat(Users(context.Targets.ResolveUsers(context, [], WiredSources.AllRoom, raw: true))).Distinct().ToArray();
        IEnumerable<WiredVariableHolder> Select(WiredVariableTarget target, int source, IEnumerable<uint> picked) => target switch
        {
            WiredVariableTarget.Furni => Furni(context.Targets.ResolveFurni(context, picked, source)),
            WiredVariableTarget.User => Users(context.Targets.ResolveUsers(context, [], source)),
            _ => [new(target, 0, 0)]
        };
        var frame = new WiredVariableFrame(context.Room.Id, holders)
        {
            RuntimeContext = context,
            Context = parent == null ? new() : ReferenceEquals(parent.RuntimeContext?.Policy, context.Policy)
                ? parent.Context : parent.Context.CreateChild(),
            VariableChanges = context.VariableChanges,
            Trigger = Select(WiredVariableTarget.Furni, WiredSources.Trigger, []).Concat(Select(WiredVariableTarget.User, WiredSources.Trigger, [])).ToArray(),
            Signal = Select(WiredVariableTarget.Furni, WiredSources.Signal, []).Concat(Select(WiredVariableTarget.User, WiredSources.Signal, [])).ToArray(),
            ResolveSource = Select,
            Depth = context.Depth,
            ChatText = context.Event.Kind == WiredEventKind.Speech ? context.Event.Message : null
        };
        frame.Selector.AddRange(Select(WiredVariableTarget.Furni, WiredSources.Selector, []).Concat(Select(WiredVariableTarget.User, WiredSources.Selector, [])));

        return frame;
    }
    public static WiredVariableFrame Fork(WiredRuntimeContext child, WiredVariableFrame parent) => Create(child, parent);
    public static WiredVariableHolder FurniHolder(Item item) => item.IsTemporary
        ? new(WiredVariableTarget.Furni, 0, unchecked((int)item.Id), false)
        : new(WiredVariableTarget.Furni, item.Id, unchecked((int)item.Id), item.Id > 0 && item.OwnerId > 0);
    public static WiredVariableHolder UserHolder(RoomUser user) => new(WiredVariableTarget.User,
        user.IsBot ? -(long)user.VirtualId - 1 : user.HabboId, user.VirtualId, !user.IsBot);
}
