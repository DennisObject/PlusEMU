using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Triggers;

public class WiredModernTrigger : WiredModernBox, IWiredClickTrigger
{
    public WiredModernTrigger(Room room, Item item, WiredBoxDescriptor descriptor) : base(room, item, descriptor)
    {
        if (!WiredTriggerConfiguration.Events.TryGetValue(descriptor.CanonicalName, out var kind)) {
            throw new ArgumentException("Unknown trigger.", nameof(descriptor));
        }

        Events = [kind];
    }
    public IReadOnlyCollection<WiredEventKind> Events { get; }
    public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        if (WiredNativeEditorProjection.Supports(Descriptor.CanonicalName)) {
            if (!WiredNativeEditorProjection.IsBound(Item.Id, Descriptor, proposed)) {
                validated = proposed;
                error = "Invalid native trigger authority.";

                return false;
            }

            if (proposed.Origin!.Native != null) {
                return WiredNativeEditorProjection.TryValidateRuntime(Item, Descriptor, proposed, out validated, out error);
            }
        }

        if (!WiredTriggerConfiguration.TryValidate(Descriptor.CanonicalName, proposed, out validated, out error)) {
            return false;
        }

        if (Descriptor.CanonicalName == "wf_trg_recv_signal"
            && validated.FurniSources.GetValueOrDefault("items") == WiredSources.Selected
            && Instance.GetRoomItemHandler() is { } handler
            && validated.SelectedItems.Select(handler.GetItem).OfType<Item>().Any(item => !WiredStackEngine.IsSignalAntenna(item))) {
            error = "wiredfurni.error.require_antenna_furni";

            return false;
        }

        if (WiredNativeEditorProjection.Supports(Descriptor.CanonicalName)) {
            validated = WiredNativeEditorProjection.RebindLegacy(Item.Id, Descriptor, proposed, validated);
        }

        return true;
    }
    public bool HidesChat(WiredRuntimeContext context) => Descriptor.CanonicalName == "wf_trg_says_something"
        && WiredTriggerPredicates.HidesChat(context.ConfigurationOf(this));

    public override bool Execute(WiredRuntimeContext context) => Matches(context, afterSelectors: false);

    public bool CanTrigger(WiredRuntimeContext context)
    {
        if (!TryValidateConfiguration(context.ConfigurationOf(this), out var config, out _)
            || (config.FurniSources.Values.Contains(WiredSources.Selector) || config.UserSources.Values.Contains(WiredSources.Selector))
                && !Matches(context, afterSelectors: true)) {
            return false;
        }

        if (Descriptor.CanonicalName == "wf_trg_score_achieved") {
            context.Selected.UserIds.UnionWith(context.Targets.ResolveUsers(context, [], WiredSources.AllRoom, raw: true)
                .Where(user => !user.IsBot && (int)user.Team == context.Event.Team).Select(user => user.VirtualId));
        }

        return true;
    }

    private bool Matches(WiredRuntimeContext context, bool afterSelectors)
    {
        if (!Events.Contains(context.Event.Kind)) {
            return false;
        }

        var config = context.ConfigurationOf(this);

        if (!TryValidateConfiguration(config, out config, out _)) {
            return false;
        }

        var evt = context.Event;
        var name = Descriptor.CanonicalName;
        // The selector pool is empty until the stack's selectors run, so those checks wait for CanTrigger.
        var deferItems = !afterSelectors && config.FurniSources.GetValueOrDefault("items") == WiredSources.Selector;
        var deferBots = !afterSelectors && config.UserSources.GetValueOrDefault("bots") == WiredSources.Selector;
        Item[] Items() => Furni(context, config, "items");
        bool ItemMatches(bool state = false) => evt.EventItem is { } item
            && (deferItems || WiredTriggerPredicates.MatchesItem(config, item, Items(), state));
        bool BotMatches() => evt.Actor?.IsBot == true && !evt.Actor.IsPet
            && (deferBots || (config.UserSources["bots"] == 0
                ? context.Targets.AllUsers().Any(user => ReferenceEquals(user, evt.Actor))
                : Users(context, config, "bots", config.Text).Contains(evt.Actor)));

        return name switch
        {
            "wf_trg_enter_room" or "wf_trg_leave_room" => evt.Actor != null && WiredTriggerPredicates.MatchesName(config, WiredModernCondition.Name(evt.Actor)),
            "wf_trg_says_something" => evt.Actor != null && WiredTriggerPredicates.MatchesChat(config, evt.Message, evt.Actor.HabboId == context.Room.OwnerId),
            "wf_trg_walks_on_furni" or "wf_trg_walks_off_furni" or "wf_trg_click_furni" => evt.Actor != null && ItemMatches(),
            "wf_trg_stuff_state" or "wf_trg_state_changed" => ItemMatches(true),
            "wf_trg_click_tile" => evt.Actor != null && (deferItems
                || Items().Any(item => WiredRoomOperations.Footprint(item, item.GetX, item.GetY, item.Rotation).Contains(new(evt.X, evt.Y)))),
            "wf_trg_click_user" => evt.Actor != null && evt.TargetUser != null,
            "wf_trg_bot_reached_avtr" => evt.TargetUser != null && BotMatches(),
            "wf_trg_bot_reached_stf" => BotMatches() && (config.SelectedItems.IsEmpty && config.FurniSources["items"] == 100 || ItemMatches()),
            "wf_trg_clock_counter" => ItemMatches() && WiredTriggerPredicates.MatchesCounter(config, evt.PreviousValue, evt.Value),
            "wf_trg_score_achieved" => WiredTriggerPredicates.MatchesScore(config, evt.Team, (int)evt.PreviousValue, (int)evt.Value),
            "wf_trg_user_performs_action" => evt.Actor != null && WiredTriggerPredicates.MatchesAction(config, evt.Action, evt.Code),
            "wf_trg_recv_signal" => context.Signal != null && (deferItems || Items().Any(item => item.Id == unchecked((uint)evt.Code))),
            "wf_trg_game_starts" or "wf_trg_game_ends" => true,
            "wf_trg_collision" => evt.Actor != null && evt.EventItem != null,
            "wf_trg_at_given_time" or "wf_trg_at_time_long" or "wf_trg_periodically" or "wf_trg_period_short" or "wf_trg_period_long" =>
                ReferenceEquals(evt.EventItem, Item),
            _ => throw new InvalidOperationException("Trigger has no event predicate.")
        };
    }

    public (bool BlockMenu, bool DoNotRotate) ClickSettings(WiredRuntimeContext context) =>
        Descriptor.CanonicalName == "wf_trg_click_user" ? (Param(context.ConfigurationOf(this), 0) == 1, Param(context.ConfigurationOf(this), 1) == 1) : (false, false);
}

public sealed class WiredModernTimedTrigger : WiredModernTrigger, IWiredTimedTrigger
{
    private readonly WiredTimedTriggers _timers = new();
    private long _started;
    private long _epoch;
    public WiredModernTimedTrigger(Room room, Item item, WiredBoxDescriptor descriptor) : base(room, item, descriptor)
    {
        if (!WiredTriggerConfiguration.IsTimed(descriptor.CanonicalName)) {
            throw new ArgumentException("Not a timed trigger.", nameof(descriptor));
        }
    }
    public WiredRuntimeEvent? Poll(long nowMilliseconds)
    {
        if (!TryValidateConfiguration(Configuration, out var config, out _)) {
            return null;
        }

        return _timers.TryFire(Descriptor.CanonicalName, Item.Id, config, nowMilliseconds,
            Math.Max(0, nowMilliseconds - _started), _epoch)
            ? new(Events.Single()) { EventItem = Item } : null;
    }
    public void Reset(long nowMilliseconds)
    {
        ResetElapsed(nowMilliseconds);
        _timers.Forget(Item.Id);
    }
    public void ResetElapsed(long nowMilliseconds)
    {
        _started = nowMilliseconds;
        _epoch++;
    }
}
