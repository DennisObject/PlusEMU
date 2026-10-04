using Plus.HabboHotel.Permissions;
using System.Collections.Immutable;
using System.Globalization;
using NLog;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.Utilities;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public sealed class WiredModernAction : WiredModernBox, IWiredContextualAction
{
    private readonly WiredCounterController _clocks;
    private readonly Action<WiredRuntimeEvent> _publish;
    private readonly WiredRoomMovement _movement;
    private readonly WiredRoomLog _roomLog;
    private readonly WiredDirectionalActions _directions = new();
    private static readonly Logger Log = LogManager.GetLogger("Wired");
    public static readonly IReadOnlySet<string> OtherNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "wf_act_control_clock", "wf_act_adjust_clock", "wf_act_reset_timers", "wf_act_call_stacks", "wf_act_neg_call_stacks",
        "wf_act_send_signal", "wf_act_neg_send_signal", "wf_act_log", "wf_act_neg_log", "wf_act_show_message", "wf_act_click_conf",
        "wf_act_chase", "wf_act_flee", "wf_act_move_to_dir", "wf_act_move_rotate_user", "wf_act_freeze", "wf_act_unfreeze",
        "wf_act_join_team", "wf_act_leave_team", "wf_act_give_score", "wf_act_give_score_tm", "wf_act_kick_user", "wf_act_mute_triggerer", "wf_act_teleport_to_room", "wf_act_give_reward"
    };
    public static bool Supports(string name) => WiredTemporaryFurnitureActions.Supports(name) || WiredMovementActions.Names.Contains(name) || OtherNames.Contains(name) || WiredBotActions.Names.Contains(name);
    public bool IsNegative => Descriptor.CanonicalName is "wf_act_neg_call_stacks" or "wf_act_neg_send_signal" or "wf_act_neg_log";
    public WiredModernAction(Room room, Item item, WiredBoxDescriptor descriptor, WiredCounterController clocks,
        Action<WiredRuntimeEvent> publish, Action<RoomUser, IEnumerable<Item>, IEnumerable<Item>> walkTransition, WiredRoomLog roomLog) : base(room, item, descriptor)
    {
        if (!Supports(descriptor.CanonicalName)) throw new ArgumentException("Unknown action.", nameof(descriptor));
        _clocks = clocks; _publish = publish; _movement = new(walkTransition);
        _roomLog = roomLog;
    }
    public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        var name = Descriptor.CanonicalName;
        if (name == "wf_act_give_reward") return WiredRewards.TryValidate(proposed, out validated, out error);
        if (WiredTemporaryFurnitureActions.Supports(name)) return WiredTemporaryFurnitureActions.TryValidate(name, proposed, out validated, out error);
        if (name == "wf_act_teleport_to_room") return WiredRoomForwarding.TryValidate(proposed, out validated, out error);
        if (WiredBotActions.Names.Contains(name)) return WiredBotActions.TryValidate(name, proposed, out validated, out error);
        if (WiredMovementActions.Names.Contains(name)) return WiredMovementConfiguration.TryValidate(name, proposed, out validated, out error);
        validated = proposed; error = "Invalid action configuration.";
        if (!WiredLegacyProtocol.IsWithinLimits(proposed)) return false;
        var p = proposed.IntParams;
        bool F(int i) => p[i] is 0 or 100 or 200 or 201;
        bool U(int i) => p[i] is 0 or 10 or 11 or 200 or 201;
        var furni = ImmutableDictionary.CreateBuilder<string, int>();
        var users = ImmutableDictionary.CreateBuilder<string, int>();
        var secondary = proposed.SecondarySelectedItems;
        switch (name)
        {
            case "wf_act_join_team":
                if (p.Length != 4 || p[0] is < 0 or > 2 || p[1] is < 1 or > 4 || !U(2) || p[3] is < 0 or > 2) return false; users["users"] = p[2]; break;
            case "wf_act_leave_team": case "wf_act_kick_user":
                if (p.Length != 1 || !U(0)) return false; users["users"] = p[0]; break;
            case "wf_act_give_score": case "wf_act_give_score_tm":
                if (p.Length != 3 || p[0] is < 1 or > 1000 || p[1] is < 0 or > 1 || (name == "wf_act_give_score" ? !U(2) : p[2] is < 1 or > 4)) return false;
                if (name == "wf_act_give_score") users["users"] = p[2]; break;
            case "wf_act_mute_triggerer":
                if (p.Length != 2 || p[0] is < 1 or > 100000 || !U(1)) return false; users["users"] = p[1]; break;
            case "wf_act_freeze":
                if (p.Length != 3 || p[0] is not (0 or 218 or 12 or 11 or 53 or 163) || p[1] is < 0 or > 1 || !U(2)) return false;
                users["users"] = p[2]; break;
            case "wf_act_unfreeze":
                if (p.Length != 1 || !U(0)) return false; users["users"] = p[0]; break;
            case "wf_act_chase": case "wf_act_flee":
                if (p.Length != 1 || !F(0)) return false; furni["items"] = p[0]; break;
            case "wf_act_move_to_dir":
                if (p.Length != 4 || p[0] is < 0 or > 7 || p[1] is < 0 or > 6 || !F(2) || p[3] is < 0 or > 1) return false;
                furni["items"] = p[2]; break;
            case "wf_act_move_rotate_user":
                if (p.Length != 3 || p[0] is < -1 or > 7 || p[1] is < -1 or > 9 || !U(2)) return false;
                users["users"] = p[2]; break;
            case "wf_act_control_clock":
                if (p.Length != 2 || p[0] is < 0 or > 4 || !F(1)) return false; furni["items"] = p[1]; break;
            case "wf_act_adjust_clock":
                if (p.Length != 4 || p[0] is < 0 or > 2 || !F(1) || p[2] is < 0 or > 99 || p[3] is < 0 or > 119) return false;
                furni["items"] = p[1]; break;
            case "wf_act_reset_timers":
                if (p.Length != 0) return false; furni["items"] = 900; break;
            case "wf_act_call_stacks": case "wf_act_neg_call_stacks":
                if (p.Length != 1 || !F(0)) return false; furni["items"] = p[0]; break;
            case "wf_act_log": case "wf_act_neg_log":
                if (p.Length != 2 || p[0] is < 0 or > 3 || !U(1)) return false;
                users["users"] = p[1]; break;
            case "wf_act_show_message":
                if (p.Length is not (3 or 4) || !U(0) || p[1] is < 0 or > 1 || p[2] is < 0 or > 1000
                    || p.Length == 4 && p[3] is < -1 or > 2) return false; users["users"] = p[0]; break;
            case "wf_act_click_conf":
                if (p.Length != 3 || p[0] is < 0 or > 2 || p[1] is < 0 or > 1 || !U(2)) return false; users["users"] = p[2]; break;
            case "wf_act_send_signal": case "wf_act_neg_send_signal":
                if (p.Length != 6 || p[0] < 0 || !F(1) || !U(2) || p[3] is < 0 or > 1 || p[4] is < 0 or > 1 || p[5] != 0) return false;
                var ids = ImmutableArray.CreateBuilder<uint>();
                foreach (var token in proposed.Text.Split([';', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (ids.Count >= 100 || !uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0) return false;
                    ids.Add(id);
                }
                secondary = ids.Distinct().ToImmutableArray();
                furni["items"] = 100; furni["forwarded"] = p[1]; users["users"] = p[2]; break;
            default: return false;
        }
        validated = proposed with { FurniSources = furni.ToImmutable(), UserSources = users.ToImmutable(), SecondarySelectedItems = secondary };
        error = ""; return true;
    }
    public override bool Execute(WiredRuntimeContext context)
    {
        var config = context.ConfigurationOf(this);
        if (!TryValidateConfiguration(config, out config, out _)) return false;
        var name = Descriptor.CanonicalName;
        if (name == "wf_act_give_reward") return WiredRewards.Execute(Item, context, config);
        if (WiredTemporaryFurnitureActions.Supports(name)) return WiredTemporaryFurnitureActions.Execute(name, Item, context, config);
        if (name == "wf_act_teleport_to_room") return WiredRoomForwarding.Execute(context, config);
        if (WiredBotActions.Names.Contains(name)) return WiredBotActions.Execute(name, context, config, _movement);
        if (WiredMovementActions.Names.Contains(name))
            return new WiredMovementActions().Execute(name, config,
                config.FurniSources.ContainsKey("movers") ? Furni(context, config, "movers") : [],
                config.FurniSources.ContainsKey("targets") ? Furni(context, config, "targets", name == "wf_act_furni_to_furni") : [],
                config.UserSources.ContainsKey("users") ? Users(context, config, "users") : [],
                (item, x, y, rotation, height) => _movement.MoveFurniture(context, item, x, y, rotation, height),
                (user, target, slide, fast, walkMode) => slide
                    ? _movement.MoveAvatar(context, user, target.GetX, target.GetY, true, walkMode)
                    : Teleport(context, user, target, fast),
                (item, state) => { item.LegacyDataString = state; item.UpdateState(); _publish(new(WiredEventKind.StateChanged) { Actor = context.Event.Actor, EventItem = item }); },
                (item, x, y, rotation, height) => _movement.MoveFurniture(context, item, x, y, rotation, height, blockOnUserCollision: true));
        var items = config.FurniSources.ContainsKey("items") ? Furni(context, config, "items") : [];
        var changed = false;
        switch (name)
        {
            case "wf_act_join_team": case "wf_act_leave_team":
                foreach (var user in Users(context, config, "users"))
                    changed |= name == "wf_act_join_team"
                        ? WiredGameState.For(context.Room).Join(context.Room, user, Param(config, 0), (Team)Param(config, 1), Param(config, 3), context.Targets.AllUsers())
                        : WiredGameState.For(context.Room).Leave(context.Room, user);
                return changed;
            case "wf_act_give_score": case "wf_act_give_score_tm":
                var amount = Param(config, 0) * (Param(config, 1) == 1 ? -1 : 1);
                if (name == "wf_act_give_score_tm")
                {
                    var playerId = context.Event.Actor is { IsBot: false } actor ? actor.HabboId : 0;
                    if (config.ScoreQuotaPerGame.HasValue && playerId == 0) return false;
                    return WiredGameState.For(context.Room).GiveScore(context.Room, Item.Id, playerId, (Team)Param(config, 2), amount, config.ScoreQuotaPerGame, _publish);
                }
                foreach (var user in Users(context, config, "users").Where(user => !user.IsBot))
                    changed |= WiredGameState.For(context.Room).GiveScore(context.Room, Item.Id, user.HabboId, user.Team, amount, config.ScoreQuotaPerGame,
                        score => _publish(score with { Actor = user }));
                return changed;
            case "wf_act_kick_user": case "wf_act_mute_triggerer":
                foreach (var user in Users(context, config, "users").Where(user => !user.IsBot))
                {
                    var client = user.GetClient(); var player = client?.GetHabbo();
                    if (player == null || client == null || player.Id == context.Room.OwnerId || player.Access.Can(PermissionKeys.ModerationTool)) continue;
                    if (config.Text.Length > 0) client.Send(new WiredChatComposer(user.VirtualId, FormatLegacyText(context, user, config.Text), 34, -1, true));
                    if (name == "wf_act_kick_user") context.Room.GetRoomUserManager().RemoveUserFromRoom(client, true, true);
                    else context.Room.MutedUsers[player.Id] = UnixTimestamp.GetNow() + Param(config, 0) * 60;
                    changed = true;
                }
                return changed;
            case "wf_act_freeze": case "wf_act_unfreeze":
                var avatarState = WiredAvatarState.For(context.Room);
                foreach (var user in Users(context, config, "users"))
                    changed |= name == "wf_act_freeze" ? avatarState.FreezeUser(user, Param(config, 0), Param(config, 1) == 1) : avatarState.Thaw(user);
                return changed;
            case "wf_act_chase": case "wf_act_flee":
                foreach (var item in items)
                {
                    var nearest = WiredDirectionalActions.Nearest(item, context.Targets.AllUsers());
                    if (nearest == null)
                    {
                        if (name == "wf_act_flee") continue;
                        var random = WiredRoomOperations.Offset(Random.Shared.Next(4) * 2);
                        changed |= _movement.MoveFurniture(context, item, item.GetX + random.X, item.GetY + random.Y, item.Rotation, null);
                        continue;
                    }
                    if (name == "wf_act_chase" && Math.Max(Math.Abs(nearest.X - item.GetX), Math.Abs(nearest.Y - item.GetY)) <= 1)
                    {
                        _publish(new(WiredEventKind.Collision) { Actor = nearest, EventItem = item }); changed = true; continue;
                    }
                    var candidates = WiredDirectionalActions.Steps(item, nearest, name == "wf_act_flee").ToArray();
                    if (name == "wf_act_flee" && candidates.Length == 0) candidates = [new(item.GetX + Random.Shared.Next(-1, 2), item.GetY)];
                    foreach (var candidate in name == "wf_act_chase" ? candidates.Take(1) : candidates)
                        if (_movement.MoveFurniture(context, item, candidate.X, candidate.Y, item.Rotation, null)) { changed = true; break; }
                }
                return changed;
            case "wf_act_move_to_dir":
                _directions.Retain(context.Targets.AllFurni());
                foreach (var item in items)
                    changed |= _directions.MoveHeading(item, Param(config, 0), Param(config, 1), Param(config, 3) == 1,
                        (x, y) => _movement.MoveFurniture(context, item, x, y, item.Rotation, null),
                        (x, y) => context.Room.GetGameMap().ValidTile(x, y) ? context.Room.GetGameMap().GetRoomUsers(new(x, y)).ToArray() : [],
                        (furni, actor) => _publish(new(WiredEventKind.Collision) { Actor = actor, EventItem = furni }));
                return changed;
            case "wf_act_move_rotate_user":
                foreach (var user in Users(context, config, "users"))
                {
                    if (Param(config, 0) >= 0)
                    {
                        var offset = WiredRoomOperations.Offset(Param(config, 0));
                        changed |= _movement.MoveAvatar(context, user, user.X + offset.X, user.Y + offset.Y, true);
                    }
                    if (Param(config, 1) < 0) continue;
                    var rotation = WiredDirectionalActions.AvatarRotation(user.RotBody, Param(config, 1));
                    if (user.RotBody == rotation && user.RotHead == rotation) continue;
                    user.RotBody = rotation; user.RotHead = rotation; user.UpdateNeeded = true; changed = true;
                }
                return changed;
            case "wf_act_control_clock":
                foreach (var item in items) changed |= _clocks.Control(item, Param(config, 0), context.NowMilliseconds);
                return changed;
            case "wf_act_adjust_clock":
                foreach (var item in items) changed |= _clocks.Adjust(item, Param(config, 0), Param(config, 2), Param(config, 3));
                return changed;
            case "wf_act_reset_timers":
                context.Room.LastTimerReset = DateTime.Now;
                context.Operations.ResetTimers(items); return true;
            case "wf_act_call_stacks": case "wf_act_neg_call_stacks":
                return context.Operations.CallStacks(context, items.Where(item => item.GetX != Item.GetX || item.GetY != Item.GetY), IsNegative);
            case "wf_act_send_signal": case "wf_act_neg_send_signal":
                var forwarded = Furni(context, config, "forwarded", true).Select(item => item.Id).ToArray();
                var users = Users(context, config, "users").Select(user => user.VirtualId).ToArray();
                var furniBatches = Param(config, 3) == 1 && forwarded.Length != 0 ? forwarded.Select(id => new[] { id }).ToArray() : [forwarded];
                var userBatches = Param(config, 4) == 1 && users.Length != 0 ? users.Select(id => new[] { id }).ToArray() : [users];
                foreach (var furni in furniBatches) foreach (var avatars in userBatches)
                    changed |= context.Operations.SendSignal(context, items, new(furni, avatars), IsNegative);
                return changed;
            case "wf_act_log": case "wf_act_neg_log":
                if (config.Text.Length == 0) return false;
                var message = context.Policy.FormatText(context, config.Text);
                _roomLog.Append(Param(config, 0), Item.Id, message, DateTimeOffset.UtcNow);
                Log.Log(Param(config, 0) switch { 0 => LogLevel.Debug, 1 => LogLevel.Info, 2 => LogLevel.Warn, _ => LogLevel.Error }, message);
                return true;
            case "wf_act_show_message":
                if (config.Text.Length == 0) return false;
                foreach (var user in Users(context, config, "users"))
                {
                    var client = user.GetClient();
                    if (client == null) continue;
                    var packet = new WiredChatComposer(user.VirtualId, FormatLegacyText(context, user, config.Text), Param(config, 2), Param(config, 3, -1), Param(config, 1) == 0);
                    if (packet.Private) client.Send(packet); else context.Room.SendPacket(packet);
                    changed = true;
                }
                return changed;
            case "wf_act_click_conf":
                foreach (var user in Users(context, config, "users"))
                {
                    var client = user.GetClient(); if (client == null) continue;
                    client.Send(new WiredClickSettingsComposer(Param(config, 0), Param(config, 1))); changed = true;
                }
                return changed;
            default: throw new InvalidOperationException("Action has no executor.");
        }
    }

    private bool Teleport(WiredRuntimeContext context, RoomUser user, Item target, bool fast)
    {
        if (user.X == target.GetX && user.Y == target.GetY) return false;
        var delay = fast ? 100 : 500; // Polaris default 500ms; fast uses max(75, delay / 5).
        var scheduled = context.Room.GetWired().ScheduleAux(context, delay, () =>
        {
            if (context.Targets.ResolveFurni(context, [target.Id], 100, raw: true).Any(attached => ReferenceEquals(attached, target)))
            {
                WiredAvatarState.For(context.Room).Thaw(user, teleport: true);
                _movement.MoveAvatar(context, user, target.GetX, target.GetY, false);
            }
        });
        if (!scheduled) return false;
        if (user.IsBot) return true; // Plus bot effects have no durable current-effect state to lease.
        var restore = WiredTemporaryEffects.For(context.Room).Acquire(user,
            () => user.IsBot ? 0 : user.GetClient()?.GetHabbo()?.Effects?.CurrentEffect ?? 0, user.ApplyEffect,
            () => context.Targets.ResolveUsers(context, [user.VirtualId], 100, raw: true).Any(attached => ReferenceEquals(attached, user)));
        void RestoreEffect() => restore();
        if (!context.Room.GetWired().ScheduleAux(context, fast ? 500 : 1500, RestoreEffect, RestoreEffect)) RestoreEffect();
        return true;
    }

    private static string FormatLegacyText(WiredRuntimeContext context, RoomUser user, string text) =>
        context.Policy.FormatText(context, text.Replace("%USERNAME%", user.GetUsername(), StringComparison.Ordinal)
            .Replace("%ROOMNAME%", context.Room.Name ?? "", StringComparison.Ordinal)
            .Replace("%USERCOUNT%", context.Room.UserCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%USERSONLINE%", PlusEnvironment.Game.ClientManager.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
}
