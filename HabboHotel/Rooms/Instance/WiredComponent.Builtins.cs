using System.Globalization;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    internal long? ReadBuiltin(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        if (frame.RoomId != _room.Id || !frame.Contains(holder)) {
            return null;
        }

        var key = RoomWiredBuiltinVariables.Normalize(reference.Token);

        if (Plus.HabboHotel.Items.Wired.Chests.WiredChestVariables.Supports(holder.Target, key)) {
            return Plus.HabboHotel.Items.Wired.Chests.WiredChestVariables.Read(Chests, frame.RuntimeContext, holder, key, _room);
        }

        if (holder.Target == WiredVariableTarget.Context) {
            var evt = frame.RuntimeContext?.Event;

            if (evt == null) {
                return null;
            }

            if (evt.Kind == WiredEventKind.Variable && evt.VariableChange is { } change) {
                return key switch
                {
                    "@event.variable_update.box_id" => change.Key.DefinitionId,
                    "@event.variable_update.change_type" => (int)change.Kind,
                    "@event.variable_update.old_value" => change.Before?.Value ?? 0,
                    "@event.variable_update.new_value" => change.After?.Value ?? 0,
                    "@event.variable_update.difference" => unchecked((change.After?.Value ?? 0) - (change.Before?.Value ?? 0)),
                    "@event.variable_update.change_origin" => change.Origin == 1 ? 3 : change.Origin == 2 ? 2 : change.RoomId != _room.Id ? 1 : 0,
                    _ => null
                };
            }

            return key switch
            {
                "@event.signal.antenna_id" when evt.Kind == WiredEventKind.Signal => unchecked((uint)evt.Code),
                "@event.chat.type" when evt.Kind == WiredEventKind.Speech => evt.ChatType,
                "@event.chat.style" when evt.Kind == WiredEventKind.Speech => evt.ChatStyle,
                "@event.link.source_room_id" when evt.Kind == WiredEventKind.Enter && evt.Actor != null => evt.Actor.WiredRoomEntry.SourceRoomId,
                _ => null
            };
        }

        if (holder.Target == WiredVariableTarget.Global) {
            if (TryGlobalTeam(key, out var team, out var score)) {
                return score ? _room.GetGameManager().Points[(int)team]
                    : _room.GetRoomUserManager().GetRoomUsers().Count(user => !user.IsBot && user.Team == team);
            }

            var instant = _clock.GetUtcNow();

            if (key == "@wired_timer") {
                _room.LastTimerResetAt ??= instant;

                return Math.Max(0, (instant - _room.LastTimerResetAt.Value).Ticks / TimeSpan.TicksPerMillisecond / 500);
            }

            var local = TimeZoneInfo.ConvertTime(instant, EffectiveTimeZone);

            return key switch
            {
                "@group_id" => _room.Group?.Id ?? 0,
                "@current_time" => instant.ToUnixTimeMilliseconds(),
                "@current_time.milliseconds_of_seconds" => local.Millisecond,
                "@current_time.seconds_of_minute" => local.Second,
                "@current_time.minute_of_hour" => local.Minute,
                "@current_time.hour_of_day" => local.Hour,
                "@current_time.day_of_week" => ((int)local.DayOfWeek + 6) % 7 + 1,
                "@current_time.day_of_month" => local.Day,
                "@current_time.day_of_year" => local.DayOfYear,
                "@current_time.week_of_year" => ISOWeek.GetWeekOfYear(local.DateTime),
                "@current_time.month_of_year" => local.Month,
                "@current_time.year" => local.Year,
                _ => null
            };
        }

        if (holder.Target == WiredVariableTarget.User) {
            var user = _room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);

            if (user == null || user.IsBot || WiredVariableRuntimeFrames.UserHolder(user) != holder
                || frame.RuntimeContext is { } userContext && (!userContext.UserIdentity.TryGetValue(user.VirtualId, out var visit)
                    || !ReferenceEquals(visit, user))) {
                return null;
            }

            return key switch
            {
                "@room_entry.method" => (int)user.WiredRoomEntry.Method,
                "@room_entry.teleport_id" => unchecked((int)user.WiredRoomEntry.TeleporterId),
                _ => null
            };
        }

        if (holder.Target != WiredVariableTarget.Furni) {
            return null;
        }

        var item = _room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));

        if (item == null || WiredVariableRuntimeFrames.FurniHolder(item) != holder
            || frame.RuntimeContext is { } itemContext && (!itemContext.FurniIdentity.TryGetValue(item.Id, out var captured)
                || !ReferenceEquals(captured, item))) {
            return null;
        }

        if (key.StartsWith("~clock.", StringComparison.Ordinal)) {
            return key switch
            {
                "~clock.state" => _counters.ReadState(item),
                "~clock.pulse_count" => _counters.ReadPulseCount(item),
                "~clock.is_game_aware" => _counters.ReadGameAware(item) == true ? 1 : null,
                _ => null
            };
        }

        if (item.IsWallItem && item.RoomId != _room.Id) {
            return null;
        }

        if (WiredWallBuiltinValues.TryReadInspection(_room, item, key, holder, frame, out var inspected)) {
            return inspected;
        }

        if (item.IsWallItem && WiredWallSnapshot.TryParse(item.WallCoordinates, out var position)) {
            return WiredWallBuiltinValues.ReadPlacement(position!, key == "@altitude"
                ? WiredWallGeometry.CaptureAltitudeInputs(_room.GetGameMap().StaticModel, position!) : default, key);
        }

        return WiredProjectileFlights.For(_room).Read(item, key,
            frame.RuntimeContext?.NowMilliseconds ?? _engine.NowMilliseconds);
    }

    public void RecordRoomNetworkForward(RoomUser actor, uint destinationRoomId) => _engine.Mutate(() =>
    {
        if (actor.IsBot || !ReferenceEquals(_room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor)) {
            return false;
        }

        var player = actor.GetClient()?.GetHabbo();

        if (player == null) {
            return false;
        }

        player.WiredRoomNetworkDestination = player.IsTeleporting ? 0 : destinationRoomId;

        return true;
    });

    private static bool TryGlobalTeam(string key, out Team team, out bool score)
    {
        team = key switch
        {
            "@teams.red.score" or "@teams.red.size" => Team.Red,
            "@teams.green.score" or "@teams.green.size" => Team.Green,
            "@teams.blue.score" or "@teams.blue.size" => Team.Blue,
            "@teams.yellow.score" or "@teams.yellow.size" => Team.Yellow,
            _ => Team.None
        };
        score = key.EndsWith(".score", StringComparison.Ordinal);

        return team != Team.None;
    }

    internal bool WriteBuiltin(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame)
    {
        var context = frame.RuntimeContext;

        if (context == null || !ReferenceEquals(context.Room, _room)) {
            return false;
        }

        var key = RoomWiredBuiltinVariables.Normalize(reference.Token);

        if (holder.Target == WiredVariableTarget.Global && frame.Contains(holder)
            && TryGlobalTeam(key, out var team, out var score) && score && value >= 0) {
            var game = _room.GetGameManager();
            var difference = (long)value - game.Points[(int)team];

            if (difference is < int.MinValue or > int.MaxValue) {
                return false;
            }

            game.AddPointToTeam(team, (int)difference);

            return true;
        }

        var movement = new WiredRoomMovement(DispatchWalkTransition);

        if (holder.Target == WiredVariableTarget.Furni) {
            var item = _room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));

            if (item == null || WiredVariableRuntimeFrames.FurniHolder(item) != holder
                || !context.FurniIdentity.TryGetValue(item.Id, out var captured) || !ReferenceEquals(captured, item)) {
                return false;
            }

            if (key == "~clock.pulse_count") {
                return _counters.SetPulseCount(item, value);
            }

            if (item.IsWallItem) {
                var changed = movement.WriteWallBuiltin(context, item, key, value);

                if (changed && _variables?.IsValueCreated == true) {
                    _variables.Value.InvalidateFx();
                }

                return changed;
            }

            if (!item.IsFloorItem) {
                return false;
            }

            return key switch
            {
                "@position" => movement.MoveFurniture(context, item, (value >> 8) & 255, value & 255, item.Rotation, null),
                "@occupation" => movement.MoveFurniture(context, item, (value >> 16) & 255, (value >> 8) & 255,
                    (value & 255) <= 7 ? value & 255 : item.Rotation, null),
                "@position.x" => movement.MoveFurniture(context, item, value, item.GetY, item.Rotation, null),
                "@position.y" => movement.MoveFurniture(context, item, item.GetX, value, item.Rotation, null),
                "@rotation" when value is >= 0 and <= 7 => movement.MoveFurniture(context, item, item.GetX, item.GetY, value, null),
                "@altitude" => movement.MoveFurniture(context, item, item.GetX, item.GetY, item.Rotation, value / 100.0),
                _ => false
            };
        }

        if (holder.Target == WiredVariableTarget.User) {
            var user = _room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);

            if (user == null || WiredVariableRuntimeFrames.UserHolder(user) != holder
                || !context.UserIdentity.TryGetValue(user.VirtualId, out var captured) || !ReferenceEquals(captured, user)) {
                return false;
            }

            return key switch
            {
                "@position" => movement.MoveAvatar(context, user, (value >> 8) & 255, value & 255, true),
                "@position.x" => movement.MoveAvatar(context, user, value, user.Y, true),
                "@position.y" => movement.MoveAvatar(context, user, user.X, value, true),
                "@direction" when value is >= 0 and <= 7 => Rotate(user, value),
                _ => false
            };
        }

        return false;
    }

    // Module.Change invokes this completion only after releasing its value lock.
    internal void PublishBuiltinStateChanged(Item item, WiredVariableFrame frame) => _engine.Mutate(() =>
    {
        if (frame.RoomId != _room.Id || !_targets.IsAttached(item)) {
            return false;
        }

        var context = frame.RuntimeContext;
        // The write already happened and this completion took it: a user who left is not named, the change stays.
        var actor = FurnitureStateEvents.Present(_room, context?.Event.Kind == WiredEventKind.Leave ? null : context?.Event.Actor);
        QueueRuntimeEvent(new(WiredEventKind.StateChanged) { EventItem = item, Actor = actor }, frame.Depth + 1);

        return true;
    });

    private static bool Rotate(RoomUser user, int direction)
    {
        user.SetRot(direction, false);
        user.UpdateNeeded = true;

        return true;
    }
}
