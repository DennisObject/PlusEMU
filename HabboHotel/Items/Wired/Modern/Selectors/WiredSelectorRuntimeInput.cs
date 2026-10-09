using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using RuntimeSources = Plus.HabboHotel.Items.Wired.Configuration.WiredSources;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

public sealed record WiredSelectorVariableQueries(Func<string, WiredConfiguration, uint, bool> FurniPredicate,
    Func<string, WiredConfiguration, int, bool> UserPredicate, Func<WiredAddonVariableRequest, long?> ReadOperand,
    Action? DisposeSession = null,
    Func<string, WiredConfiguration, uint, WiredSelectorInputs, bool>? ScopedFurniPredicate = null,
    Func<string, WiredConfiguration, int, WiredSelectorInputs, bool>? ScopedUserPredicate = null) : IDisposable
{
    public void Dispose() => DisposeSession?.Invoke();
}

public sealed record WiredSelectorRuntimeInput(WiredSelectorWorld World, WiredSelectorInputs Selection,
    Func<WiredAddonVariableRequest, long?>? ReadVariable,
    Func<int, WiredConfiguration, IEnumerable<uint>>? ResolveFurni = null,
    Func<int, IEnumerable<int>>? ResolveUsers = null)
{
    public WiredAddonInputs ForAddons(long now) => new(World, Selection, now, ReadVariable, ResolveFurni, ResolveUsers);

    public static WiredSelectorRuntimeInput Capture(WiredRuntimeContext context, WiredSelectorRoomState state,
        IGroupManager groups,
        WiredSelectorVariableQueries? variables = null,
        Func<WiredRuntimeContext, WiredSelectorWorld>? readWorld = null)
    {
        // Share the room projection within this firing; sources below remain live and identity-checked.
        var world = context.SelectorWorldSnapshot ??= readWorld?.Invoke(context) ?? CaptureWorld(context, state, groups);
        WiredSelectedIds Selection(int source)
        {
            var result = new WiredSelectedIds();
            result.FurniIds.UnionWith(context.Targets.ResolveFurni(context, [], source, raw: true).Select(x => x.Id));
            result.UserIds.UnionWith(context.Targets.ResolveUsers(context, [], source, raw: true).Select(x => x.VirtualId));

            return result;
        }
        var input = new WiredSelectorInputs(Selection(RuntimeSources.Trigger), Selection(RuntimeSources.Selector),
            Selection(RuntimeSources.Signal), context.Event.Kind == WiredEventKind.ClickUser ? context.Event.TargetUser?.VirtualId : null,
            context.NowMilliseconds, context.Event.Kind == WiredEventKind.AvatarAction ? context.Event.Actor?.VirtualId : null,
            context.Event.Kind == WiredEventKind.AvatarAction ? context.Event.Action : null, context.Event.Code,
            variables?.FurniPredicate, variables?.UserPredicate,
            (context.SelectorKinds & WiredSelectionKind.Furni) != 0, (context.SelectorKinds & WiredSelectionKind.Users) != 0, world.IncludeWired, variables?.ScopedFurniPredicate, variables?.ScopedUserPredicate);

        return new(world, input, variables?.ReadOperand,
            (source, configuration) => context.Targets.ResolveFurni(context, configuration.SelectedItems, source).Select(x => x.Id),
            source => context.Targets.ResolveUsers(context, [], source).Select(x => x.VirtualId));
    }

    public WiredSelectorRuntimeInput WithVariables(WiredSelectorVariableQueries? variables) => variables is null ? this
        : this with
        {
            Selection = Selection with
            {
                FurniVariablePredicate = variables.FurniPredicate,
                UserVariablePredicate = variables.UserPredicate,
                ScopedFurniVariablePredicate = variables.ScopedFurniPredicate,
                ScopedUserVariablePredicate = variables.ScopedUserPredicate
            },
            ReadVariable = variables.ReadOperand
        };

    private static WiredSelectorWorld CaptureWorld(WiredRuntimeContext context, WiredSelectorRoomState state,
        IGroupManager groupManager)
    {
        var items = context.Targets.ResolveFurni(context, [], RuntimeSources.AllRoom, raw: true);
        var users = context.Targets.ResolveUsers(context, [], RuntimeSources.AllRoom, raw: true);
        var remotes = new Dictionary<uint, WiredRemoteSelector>();

        foreach (var item in items) {
            if (context.Room.GetWired().TryGet(item.Id, out var box) && box is IWiredConfiguredItem configured
                && configured.Descriptor.Category == WiredBoxCategory.Selector) {
                remotes[item.Id] = new(configured.Descriptor.CanonicalName, context.ConfigurationOf(configured));
            }
        }

        var furni = items.Select(item => new WiredSelectorFurniture(item.Id, checked((int)item.Definition.Id),
            item.Definition.PublicName, item.LegacyDataString, item.GetX, item.GetY, item.GetZ,
            item.TotalHeight - item.GetZ, item.GetAffectedTiles.Values.Select(t => (t.X, t.Y))
                .Append((item.GetX, item.GetY)).Distinct().ToArray(), item.IsFloorItem, item.IsWired)).ToArray();
        var avatars = users.Select(user =>
        {
            var action = state.Read(user);
            var name = user.IsBot ? user.BotData.Name : user.GetClient()?.GetHabbo()?.Username ?? "";
            var equippedGroupId = user.IsBot ? 0 : user.GetClient()?.GetHabbo()?.HabboStats?.FavouriteGroupId ?? 0;

            return new WiredSelectorAvatar(user.VirtualId, name,
                user.IsPet ? WiredSelectorEntityKind.Pet : user.IsBot ? WiredSelectorEntityKind.Bot : WiredSelectorEntityKind.Player,
                user.X, user.Y, (int)user.Team, HandItem: user.CarryItemId,
                Sitting: user.IsSitting || user.HasStatus("sit"), Lying: user.IsLying || user.HasStatus("lay"), Idle: user.IsAsleep,
                Sign: user.Statusses.TryGetValue("sign", out var signText) && int.TryParse(signText, out var sign) ? sign : null,
                Dance: user.DanceId, LastAction: action?.Action, LastActionParameter: action?.Parameter ?? 0,
                LastActionAtMs: action?.At ?? 0, EquippedGroupId: equippedGroupId);
        }).ToArray();
        var model = context.Room.GetGameMap().Model;
        var includeWired = context.Trigger is { } trigger && items.Any(item => item.GetX == trigger.Item.GetX
            && item.GetY == trigger.Item.GetY && context.Room.GetWired().TryGet(item.Id, out var candidate)
            && candidate is IWiredConfiguredItem configured && configured.Descriptor.CanonicalName == "wf_xtra_or_eval"
            && WiredSelectorSources.Param(context.ConfigurationOf(configured), 1) == RuntimeSources.Selector);

        return new(model.MapSizeX, model.MapSizeY, furni, avatars, context.Room.Group?.Id ?? 0, remotes, includeWired);
    }
}
