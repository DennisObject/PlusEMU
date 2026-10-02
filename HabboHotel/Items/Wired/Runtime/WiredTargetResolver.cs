using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public sealed class WiredTargetResolver(Func<IEnumerable<Item>> furni, Func<IEnumerable<RoomUser>> users)
{
    public Item[] AllFurni() => furni().ToArray();
    public RoomUser[] AllUsers() => users().ToArray();

    public Item[] ResolveFurni(WiredRuntimeContext context, IEnumerable<uint> saved, int source)
    {
        IEnumerable<uint> ids = source switch
        {
            WiredSources.Trigger => context.Triggering.FurniIds,
            WiredSources.Selected or WiredSources.Snapshot => saved,
            WiredSources.Selector => context.SelectorPool.FurniIds,
            WiredSources.Signal => context.Signal?.Selection.FurniIds ?? [],
            WiredSources.AllRoom => AllFurni().Select(x => x.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown furniture source")
        };
        var wanted = ids.ToHashSet();
        return AllFurni().Where(x => wanted.Contains(x.Id) && context.FurniIdentity.TryGetValue(x.Id, out var original)
            && ReferenceEquals(x, original)).ToArray();
    }

    public RoomUser[] ResolveUsers(WiredRuntimeContext context, IEnumerable<int> saved, int source, string? name = null)
    {
        var roomUsers = AllUsers();
        IEnumerable<int> ids = source switch
        {
            WiredSources.Trigger when context.Event.Kind == WiredEventKind.BotReachedUser && context.Event.Actor?.IsBot == true
                => context.Event.TargetUser is { } reached ? [reached.VirtualId] : [],
            WiredSources.Trigger => context.Triggering.UserIds,
            WiredSources.ClickedUser when context.Event.Kind == WiredEventKind.ClickUser =>
                context.Event.TargetUser is { } clicked ? [clicked.VirtualId] : [],
            WiredSources.ClickedUser => [],
            WiredSources.ReachedUser => context.Event.TargetUser is { } target ? [target.VirtualId] : [],
            WiredSources.Selected when name == null => saved,
            WiredSources.BotByName => roomUsers.Where(x => x.IsBot && !x.IsPet
                && string.Equals(x.BotData.Name, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.VirtualId),
            WiredSources.UserByName => roomUsers.Where(x => !x.IsBot
                && string.Equals(x.GetClient()?.GetHabbo()?.Username, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.VirtualId),
            WiredSources.Selector => context.SelectorPool.UserIds,
            WiredSources.Signal => context.Signal?.Selection.UserIds ?? [],
            WiredSources.AllRoom => roomUsers.Select(x => x.VirtualId),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown avatar source")
        };
        var wanted = ids.ToHashSet();
        return roomUsers.Where(x => wanted.Contains(x.VirtualId) && context.UserIdentity.TryGetValue(x.VirtualId, out var original)
            && ReferenceEquals(x, original)).ToArray();
    }
}
