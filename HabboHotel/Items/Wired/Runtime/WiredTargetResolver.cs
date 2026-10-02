using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public sealed class WiredTargetResolver(Func<IEnumerable<Item>> furni, Func<IEnumerable<RoomUser>> users,
    Func<uint, Item?>? findFurni = null, Func<int, RoomUser?>? findUser = null)
{
    public Item[] AllFurni() => furni().ToArray();
    public RoomUser[] AllUsers() => users().ToArray();

    public Item[] ResolveFurni(WiredRuntimeContext context, IEnumerable<uint> saved, int source, bool raw = false)
    {
        var savedIds = saved.Distinct().Order().ToArray();
        IEnumerable<uint> ids = source switch
        {
            WiredSources.Trigger => context.Triggering.FurniIds,
            WiredSources.Selected or WiredSources.Snapshot => savedIds,
            WiredSources.Selector => context.SelectorPool.FurniIds,
            WiredSources.Signal => context.Signal?.Selection.FurniIds ?? [],
            WiredSources.AllRoom => AllFurni().Select(x => x.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown furniture source")
        };
        if (!raw && source != WiredSources.Selector && context.Policy.Addons.FurniLimit is > 0 and var limit)
        {
            var key = (source, string.Join(',', savedIds), limit);
            if (!context.FurniSubsets.TryGetValue(key, out var subset))
            {
                subset = ids.Distinct().ToArray();
                Random.Shared.Shuffle(subset);
                subset = subset.Take(limit).ToArray();
                context.FurniSubsets[key] = subset;
            }
            ids = subset;
        }
        var wanted = ids.ToHashSet();
        var candidates = findFurni == null ? AllFurni().Where(x => wanted.Contains(x.Id))
            : wanted.Select(findFurni).OfType<Item>();
        return candidates.Where(x => wanted.Contains(x.Id) && context.FurniIdentity.TryGetValue(x.Id, out var original)
            && ReferenceEquals(x, original)).ToArray();
    }

    public RoomUser[] ResolveUsers(WiredRuntimeContext context, IEnumerable<int> saved, int source, string? name = null, bool raw = false)
    {
        var savedIds = saved.Distinct().Order().ToArray();
        var roomUsers = source == WiredSources.AllRoom || name != null ? AllUsers() : [];
        IEnumerable<int> ids = source switch
        {
            WiredSources.Trigger when context.Event.Kind == WiredEventKind.BotReachedUser && context.Event.Actor?.IsBot == true
                => context.Event.TargetUser is { } reached ? [reached.VirtualId] : [],
            WiredSources.Trigger => context.Triggering.UserIds,
            WiredSources.ClickedUser when context.Event.Kind == WiredEventKind.ClickUser =>
                context.Event.TargetUser is { } clicked ? [clicked.VirtualId] : [],
            WiredSources.ClickedUser => [],
            WiredSources.ReachedUser => context.Event.TargetUser is { } target ? [target.VirtualId] : [],
            WiredSources.Selected when name == null => savedIds,
            WiredSources.BotByName => roomUsers.Where(x => x.IsBot && !x.IsPet
                && string.Equals(x.BotData.Name, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.VirtualId),
            WiredSources.UserByName => roomUsers.Where(x => !x.IsBot
                && string.Equals(x.GetClient()?.GetHabbo()?.Username, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.VirtualId),
            WiredSources.Selector => context.SelectorPool.UserIds,
            WiredSources.Signal => context.Signal?.Selection.UserIds ?? [],
            WiredSources.AllRoom => roomUsers.Select(x => x.VirtualId),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown avatar source")
        };
        if (!raw && source != WiredSources.Selector && context.Policy.Addons.UserLimit is > 0 and var limit)
        {
            var key = (source, string.Join(',', savedIds), name, limit,
                source == WiredSources.Trigger ? string.Join(',', ids.Order()) : "");
            if (!context.UserSubsets.TryGetValue(key, out var subset))
            {
                subset = ids.Distinct().ToArray();
                Random.Shared.Shuffle(subset);
                subset = subset.Take(limit).ToArray();
                context.UserSubsets[key] = subset;
            }
            ids = subset;
        }
        var wanted = ids.ToHashSet();
        var candidates = findUser == null ? (roomUsers.Length > 0 ? roomUsers : AllUsers()).Where(x => wanted.Contains(x.VirtualId))
            : wanted.Select(findUser).OfType<RoomUser>();
        return candidates.Where(x => wanted.Contains(x.VirtualId) && context.UserIdentity.TryGetValue(x.VirtualId, out var original)
            && ReferenceEquals(x, original)).ToArray();
    }
}
