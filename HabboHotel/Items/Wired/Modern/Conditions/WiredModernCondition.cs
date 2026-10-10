using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Items.Wired.Modern.Conditions;

public sealed class WiredModernCondition : WiredModernBox
{
    private readonly WiredConfiguration? _initialCountConfiguration;
    internal bool HasInitialCountConfiguration => Descriptor.CanonicalName == "wf_cnd_user_count_in"
        && ReferenceEquals(Configuration, _initialCountConfiguration);
    private readonly Func<Item, long?> _counterTime;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IGroupManager _groups;
    public WiredModernCondition(Room room, Item item, WiredBoxDescriptor descriptor,
        IGroupManager groups, Func<Item, long?> counterTime, Func<DateTimeOffset> clock) : base(room, item, descriptor)
    {
        if (!WiredConditionConfiguration.Supports(descriptor.CanonicalName)) {
            throw new ArgumentException("Unknown condition.", nameof(descriptor));
        }

        _initialCountConfiguration = descriptor.CanonicalName == "wf_cnd_user_count_in" ? Configuration : null;
        _counterTime = counterTime;
        _clock = clock;
        _groups = groups;
    }
    public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        if (WiredNativeEditorProjection.Supports(Descriptor.CanonicalName)) {
            if (!WiredNativeEditorProjection.IsBound(Item.Id, Descriptor, proposed)) {
                validated = proposed;
                error = "Invalid native condition authority.";

                return false;
            }

            if (proposed.Origin!.Native != null) {
                return WiredNativeEditorProjection.TryValidateRuntime(Item, Descriptor, proposed, out validated, out error);
            }
        }

        if (!WiredConditionConfiguration.TryValidate(Descriptor.CanonicalName, proposed, out validated, out error)) {
            return false;
        }

        if (WiredNativeEditorProjection.Supports(Descriptor.CanonicalName)) {
            validated = WiredNativeEditorProjection.RebindLegacy(Item.Id, Descriptor, proposed, validated);
        }

        return true;
    }

    public override bool Execute(WiredRuntimeContext context)
    {
        var config = context.ConfigurationOf(this);

        if (!TryValidateConfiguration(config, out config, out _)) {
            return false;
        }

        var negative = WiredConditionConfiguration.NegativeNames.TryGetValue(Descriptor.CanonicalName, out var positive);

        // Negative labels are the empty direction: 0 is at least one empty, 1 is all empty.
        if (negative && Descriptor.CanonicalName is "wf_cnd_not_furni_on" or "wf_cnd_not_hv_avtrs") {
            config = config with { IntParams = config.IntParams.SetItem(0, config.IntParams[0] == 0 ? 1 : 0) };
        }

        var result = Evaluate(positive ?? Descriptor.CanonicalName, context, config);

        return negative ? !result : result;
    }

    private bool Evaluate(string name, WiredRuntimeContext context, WiredConfiguration config)
    {
        Item[] Items() => Furni(context, config, "items");
        RoomUser[] Avatars() => Users(context, config, "users");
        bool Quantify(Func<RoomUser, bool> predicate, int index) => WiredRoomOperations.Quantify(Avatars().Select(predicate), Param(config, index));
        var now = _clock();

        switch (name) {
            case "wf_cnd_actor_dir":
                return Quantify(user => (Param(config, 0) & (1 << user.RotBody)) != 0, 2);
            case "wf_cnd_actor_in_team":
                return Quantify(user => Param(config, 0) == 0 ? user.Team != Team.None : (int)user.Team == Param(config, 0), 2);
            case "wf_cnd_has_handitem":
                return Quantify(user => user.CarryItemId == Param(config, 0), 2);
            case "wf_cnd_wearing_effect":
                return Quantify(user => !user.IsBot && user.GetClient()?.GetHabbo()?.Effects?.CurrentEffect == Param(config, 0), 2);
            case "wf_cnd_wearing_badge":
                return Quantify(user => user.GetClient()?.GetHabbo()?.Inventory?.Badges.EquippedBadges.Any(badge =>
                string.Equals(badge.Code, config.Text, StringComparison.OrdinalIgnoreCase)) == true, 1);
            case "wf_cnd_actor_in_group":
                var group = context.Room.Group;

                if (Param(config, 1) == 1 && !_groups.TryGetGroup(Param(config, 2), out group)) {
                    return false;
                }

                return group != null && Quantify(user => !user.IsBot && group.IsMember(user.HabboId), 3);
            case "wf_cnd_user_performs_action":
                return Quantify(user => IsPerforming(user, config, context.Event), 6);
            case "wf_cnd_triggerer_match":
                var comparisons = Users(context, config, "comparison", config.UserSources["comparison"] == 101 ? config.Text : null)
                    .Where(user => MatchesKind(user, Param(config, 0)))
                    .Where(user => Param(config, 1) == 0 || string.Equals(Name(user), config.Text, StringComparison.OrdinalIgnoreCase))
                    .Select(user => user.VirtualId).ToHashSet();

                return comparisons.Count != 0 && Quantify(user => MatchesKind(user, Param(config, 0)) && comparisons.Contains(user.VirtualId), 4);
            case "wf_cnd_trggrer_on_frn":
                return WiredItemConditions.TriggererOnItems(config, Items(), Avatars());
            case "wf_cnd_furnis_hv_avtrs":
                return WiredItemConditions.HasAvatars(config, Items(), context.Targets.AllUsers());
            case "wf_cnd_has_furni_on":
                return WiredItemConditions.HasStackedFurniture(config, Items(), item => WiredRoomOperations.HasStackedItem(context.Room, item));
            case "wf_cnd_match_snapshot":
                return WiredItemConditions.MatchesSnapshot(config, Items());
            case "wf_cnd_stuff_is":
                return WiredItemConditions.MatchesType(config, Items(), Furni(context, config, "comparison", true));
            case "wf_cnd_has_altitude":
                return WiredItemConditions.MatchesAltitude(config, Items());
            case "wf_cnd_valid_moves":
                return WiredItemConditions.ValidMoves(Items(), (item, x, y) => WiredRoomOperations.CanMoveItem(context.Room, item, x, y, item.Rotation,
                    collision: WiredRoomMovement.Collision(context.Policy.Addons.Physics, step: true)));
            case "wf_cnd_slc_quantity":
                return WiredItemConditions.SelectionQuantity(config,
                Param(config, 2) == 1 ? Items().Length : 0, Param(config, 2) == 0 ? Avatars().Length : 0);
            case "wf_cnd_user_count_in":
                var count = config.UserSources["users"] == 0 ? context.Targets.AllUsers().Count(user => !user.IsBot) : Avatars().Length;

                return count >= Param(config, 0) && count <= Param(config, 1);
            case "wf_cnd_counter_time_matches":
                var times = Items().Select(_counterTime).ToArray();

                return times.Length != 0 && times.All(time => time.HasValue)
                    && WiredTimeConditions.MatchesCounter(config, times.Select(time => time!.Value));
            case "wf_cnd_match_time":
                return WiredTimeConditions.MatchesTime(config, now);
            case "wf_cnd_match_date":
                return WiredTimeConditions.MatchesDate(config, now);
            case "wf_cnd_date_rng_active":
                return WiredTimeConditions.MatchesRange(config, now);
            case "wf_cnd_time_less_than":
            case "wf_cnd_time_more_than":
                // Like Turbo's room timer, the first elapsed check starts a shared epoch.
                var resetAt = context.Room.LastTimerResetAt ??= now.ToUniversalTime();
                var elapsed = now.ToUniversalTime() - resetAt;

                return WiredTimeConditions.MatchesElapsed(config, Math.Max(0, (long)elapsed.TotalMilliseconds), name == "wf_cnd_time_more_than");
            case "wf_cnd_team_has_rank":
            case "wf_cnd_team_has_score":
                var scores = context.Room.GetGameManager().Points;
                var teams = Param(config, 0) == 0 ? Avatars().Select(user => (int)user.Team).Where(team => team is >= 1 and <= 4).Distinct().ToArray() : [Param(config, 0)];

                return WiredRoomOperations.Quantify(teams.Select(team => name == "wf_cnd_team_has_score"
                    ? WiredRoomOperations.Compare(scores[team], Param(config, 2), Param(config, 1))
                    : 1 + Enumerable.Range(1, 4).Count(other => scores[other] > scores[team]) == Param(config, 1) + 1),
                    Param(config, name == "wf_cnd_team_has_score" ? 4 : 3));
            default:
                throw new InvalidOperationException("Condition has no evaluator.");
        }
    }

    public static string Name(RoomUser user) => user.IsBot ? user.BotData.Name : user.GetClient()?.GetHabbo()?.Username ?? "";
    // Polaris' editor uses pet=2/bot=4; Turbo's internal mask reverses those bits.
    public static bool MatchesKind(RoomUser user, int mask) => (mask & (user.IsPet ? 2 : user.IsBot ? 4 : 1)) != 0;
    public static bool IsPerforming(RoomUser user, WiredConfiguration config, WiredRuntimeEvent @event)
    {
        var action = Param(config, 0);

        if (@event.Kind == WiredEventKind.AvatarAction && ReferenceEquals(@event.Actor, user)
            && Triggers.WiredTriggerPredicates.MatchesAction(config, @event.Action, @event.Code)) {
            return true;
        }

        return action switch
        {
            1 => user.HasStatus("wav"),
            4 => !user.IsAsleep,
            5 => user.IsAsleep,
            6 => user.HasStatus("sit"),
            7 => !user.HasStatus("sit") && !user.HasStatus("lay"),
            8 => user.HasStatus("lay"),
            9 => user.Statusses.TryGetValue("sign", out var sign) && (Param(config, 1) == 0 || sign == Param(config, 2).ToString()),
            10 => user.DanceId != 0 && (Param(config, 3) == 0 || user.DanceId == Param(config, 4)),
            _ => false
        };
    }
}
