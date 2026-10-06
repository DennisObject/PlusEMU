using Plus.HabboHotel.Subscriptions;
using System.Globalization;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>
/// Reads live room state. Missing engine-owned properties (projectile, sign, entry provenance, visibility)
/// are absent unless the engine supplies them; they are never fabricated as zero. Movement is supplied
/// by the stack engine so its collision, animation and carried-user policy applies to variable writes too.
/// </summary>
public sealed class RoomWiredBuiltinVariables(Room room,
    Func<WiredVariableReference, WiredVariableHolder, WiredVariableFrame, int?>? engineRead = null,
    Func<WiredVariableReference, WiredVariableHolder, int, WiredVariableFrame, bool>? engineWrite = null,
    Action<Item, WiredVariableFrame>? stateChanged = null) : IWiredBuiltinVariables
{
    /// <summary>Source capabilities, independent of whether a particular holder currently has the variable.</summary>
    public static bool HasNumericValue(WiredVariableReference reference)
    {
        var key = Normalize(reference.Token);

        return reference.Target switch
        {
            WiredVariableTarget.Furni => key is "@altitude" or "@class_id" or "@dimensions.x" or "@dimensions.y" or "@height"
                or "@id" or "@owner_id" or "@position.x" or "@position.y" or "@rotation" or "@state" or "@type" or "@wallitem_offset"
                or "@projectile.animation.furni_collisions" or "@projectile.animation.user_collisions" or "@projectile.animation.tiles_traveled"
                or "@projectile.animation.position.x" or "@projectile.animation.position.y" or "@projectile.animation.position.altitude",
            WiredVariableTarget.User => key is "@achievement_score" or "@altitude" or "@bot_id" or "@dance" or "@direction" or "@effect"
                or "@gender" or "@handitem" or "@index" or "@pet_id" or "@position.x" or "@position.y" or "@room_entry.method"
                or "@room_entry.teleport_id" or "@sign" or "@team.color" or "@team.score" or "@type" or "@user_id",
            WiredVariableTarget.Global => key is "@furni_count" or "@room_id" or "@user_count",
            WiredVariableTarget.Context => key is "@selector_furni_count" or "@selector_user_count" or "@signal_furni_count" or "@signal_user_count",
            _ => false
        };
    }

    // v2 only: legacy and shadow rooms write gate states directly and never enter the module's admission.
    public bool SequencesGateWrites => GateTransitionService.For(room) != null;

    // The gate's per-write FIFO decides: behind a pending write, or a closing from another thread, the whole
    // transaction waits for the owner. Otherwise it runs now with the transform's single, already evaluated result.
    public IDisposable? Admit(WiredVariableReference reference, WiredVariableHolder holder, ref Func<int, int> transform,
        Func<Func<int, int>, Action> replayWith, Func<bool> stillTargeted, out WiredAdmission admission)
    {
        admission = WiredAdmission.Proceed;

        if (holder.Target != WiredVariableTarget.Furni || Normalize(reference.Token) != "@state") {
            return null;
        }

        if (FindItem(holder) is not { } item || !GateTransitionService.IsGate(item) || GateTransitionService.For(room) is not { } gates) {
            return null;
        }

        var original = transform;
        string? Peek(string current) => int.TryParse(current, out var value)
            ? original(value).ToString(CultureInfo.InvariantCulture) : null;
        Action Replay(string? prepared) => replayWith(prepared is null ? original : _ => int.Parse(prepared, CultureInfo.InvariantCulture));
        var scope = gates.AdmitVariableWrite(item, Peek, Replay, stillTargeted, out admission, out var evaluated);

        if (evaluated is not null) {
            transform = _ => int.Parse(evaluated, CultureInfo.InvariantCulture);
        }

        return scope;
    }

    public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        if (reference.Target != holder.Target || frame.RoomId != room.Id || !frame.Contains(holder)) {
            return null;
        }

        var key = Normalize(reference.Token);
        int? value = holder.Target switch
        {
            WiredVariableTarget.Furni => ReadItem(key, holder),
            WiredVariableTarget.User => ReadAvatar(key, holder),
            WiredVariableTarget.Global => key switch
            {
                "@furni_count" => room.GetRoomItemHandler().GetWallAndFloor.Count(),
                "@room_id" => checked((int)room.Id),
                "@user_count" => room.GetRoomUserManager().GetRoomUsers().Count,
                _ => null
            },
            WiredVariableTarget.Context => key switch
            {
                "@selector_furni_count" => frame.Selector.Count(x => x.Target == WiredVariableTarget.Furni),
                "@selector_user_count" => frame.Selector.Count(x => x.Target == WiredVariableTarget.User),
                "@signal_furni_count" => frame.Signal.Count(x => x.Target == WiredVariableTarget.Furni),
                "@signal_user_count" => frame.Signal.Count(x => x.Target == WiredVariableTarget.User),
                _ => null
            },
            _ => null
        };
        value ??= engineRead?.Invoke(reference with { Token = $"internal:{key}" }, holder, frame);

        return value is int number ? new(number, null, null) : null;
    }

    public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame)
    {
        var changed = Write(reference, holder, value, frame, out var completed);

        if (changed) {
            completed?.Invoke();
        }

        return changed;
    }

    public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame, out Action? completed)
    {
        completed = null;

        if (reference.Target != holder.Target || frame.RoomId != room.Id || !frame.Contains(holder)) {
            return false;
        }

        var key = Normalize(reference.Token);

        if (holder.Target == WiredVariableTarget.User) {
            var avatar = room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);

            if (avatar is null || !Matches(avatar, holder)) {
                return false;
            }

            if (key == "@handitem" && value is >= 0 and <= 9999) {
                avatar.CarryItem(value);

                return true;
            }

            if (key == "@team.score" && !avatar.IsBot && avatar.Team != Team.None && value >= 0) {
                var game = room.GetGameManager();
                var current = game.Points[(int)avatar.Team];
                var difference = (long)value - current;

                if (difference is < int.MinValue or > int.MaxValue) {
                    return false;
                }

                game.AddPointToTeam(avatar.Team, (int)difference);

                return true;
            }
        }

        if (holder.Target == WiredVariableTarget.Furni && key == "@state") {
            var item = FindItem(holder);

            if (item is null || value < 0 || item.Definition.Modes <= value
                || !int.TryParse(item.LegacyDataString, out var previous) || previous == value) {
                return false;
            }

            var mark = FurnitureStateEvents.Mark();

            if (GateTransitionService.For(item) != null) {
                if (!WriteGateState(item, value)) {
                    return false;
                }
            }
            else {
                item.LegacyDataString = value.ToString(CultureInfo.InvariantCulture);
                item.UpdateState();
            }

            // The completion reports this write only; a write the room's pass already reported stays reported.
            if (stateChanged is not null && FurnitureStateEvents.TakeWriteSince(item, mark)) {
                completed = () => stateChanged(item, frame);
            }

            return true;
        }

        return engineWrite?.Invoke(reference with { Token = $"internal:{key}" }, holder, value, frame) == true;
    }

    // v2 only: a gate's state goes through its transition service.
    private bool WriteGateState(Item item, int value)
    {
        var next = value.ToString(CultureInfo.InvariantCulture);

        // Closing writes from other threads were sequenced whole by Admit; nothing is notified early.
        if (GateTransitionService.IsClosing(item, next) && !RoomOwnerScope.IsOwner(room)) {
            return false;
        }

        return GateTransitionService.WriteNow(item, next, GateCloseReason.Wired) != GateTransition.Refused;
    }

    private Item? FindItem(WiredVariableHolder holder)
    {
        var item = room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));

        return item is not null && WiredVariableRuntimeFrames.FurniHolder(item) == holder ? item : null;
    }
    private int? ReadItem(string key, WiredVariableHolder holder)
    {
        var item = FindItem(holder);

        if (item is null) {
            return null;
        }

        // Wall coordinates require the native parser and captured-reference checks in engineRead.
        if (item.IsWallItem && key is "@position.x" or "@position.y" or "@altitude" or "@rotation" or "@wallitem_offset") {
            return null;
        }

        return key switch
        {
            "@id" => unchecked((int)item.Id),
            "@owner_id" => checked((int)item.OwnerId),
            "@class_id" => item.Definition.SpriteId,
            "@height" => Hundredths(item.TotalHeight - item.GetZ),
            "@state" => int.TryParse(item.LegacyDataString, out var state) ? state : null,
            "@position.x" => item.GetX,
            "@position.y" => item.GetY,
            "@altitude" => Hundredths(item.GetZ),
            "@rotation" => item.Rotation,
            "@dimensions.x" => item.Definition.Width,
            "@dimensions.y" => item.Definition.Length,
            "@is_stackable" => Flag(item.IsFloorItem && item.Definition.Stackable),
            "@can_stand_on" => Flag(item.IsFloorItem && item.Definition.Walkable),
            "@can_sit_on" => Flag(item.IsFloorItem && item.Definition.IsSeat),
            "@can_lay_on" => Flag(item.IsFloorItem && item.Definition.InteractionType == InteractionType.Bed),
            "@type" => item.IsTemporary ? 2 : item.OwnerId > 0 ? 0 : null,
            _ => null
        };
    }
    private int? ReadAvatar(string key, WiredVariableHolder holder)
    {
        var avatar = room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);

        if (avatar is null || !Matches(avatar, holder)) {
            return null;
        }

        var habbo = avatar.IsBot ? null : avatar.GetClient()?.GetHabbo();

        return key switch
        {
            "@index" => avatar.VirtualId,
            "@type" => avatar.IsPet ? 2 : avatar.IsBot ? 4 : 1,
            "@user_id" => habbo?.Id,
            "@bot_id" => avatar.IsBot && !avatar.IsPet ? avatar.BotData.Id : null,
            "@pet_id" => avatar.IsPet ? avatar.PetData.PetId : null,
            "@position.x" => avatar.X,
            "@position.y" => avatar.Y,
            "@altitude" => Hundredths(avatar.Z),
            "@direction" => avatar.RotBody,
            "@dance" => avatar.DanceId,
            "@handitem" => avatar.CarryItemId,
            "@sign" => avatar.Statusses.TryGetValue("sign", out var sign) && int.TryParse(sign, NumberStyles.None, CultureInfo.InvariantCulture, out var signId) ? signId : -1,
            "@effect" => habbo?.Effects.CurrentEffect,
            "@gender" => habbo is null ? -1 : habbo.Gender.Equals("M", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
            "@achievement_score" => habbo?.HabboStats.AchievementPoints,
            "@is_hc" => Flag(habbo is not null && ClubAccess.LevelFor(habbo.Access) > 0),
            "@is_idle" => Flag(avatar.IsAsleep),
            "@is_frozen" => Flag(avatar.Freezed || avatar.Frozen),
            "@is_trading" => Flag(avatar.IsTrading),
            "@is_muted" => Flag(habbo is not null && habbo.TimeMuted > 0),
            "@is_owner" => Flag(habbo is not null && habbo.Id == room.OwnerId),
            "@has_rights" => Flag(habbo is not null && room.CheckRights(habbo.Client)),
            "@team.color" => !avatar.IsBot ? (int)avatar.Team : null,
            "@team.score" => !avatar.IsBot ? room.GetGameManager().Points[(int)avatar.Team] : null,
            _ => null
        };
    }
    private static bool Matches(RoomUser avatar, WiredVariableHolder holder) => avatar.IsBot
        ? !holder.CanPersist && holder.StableId == -(long)avatar.VirtualId - 1
        : holder.CanPersist && holder.StableId == avatar.HabboId;
    private static int? Flag(bool flag) => flag ? 1 : null;
    private static int Hundredths(double value) => (int)Math.Clamp(Math.Round(value * 100), int.MinValue, int.MaxValue);
    public static string Normalize(string token)
    {
        var key = token.StartsWith("internal:", StringComparison.Ordinal) ? token[9..] : token;

        return key switch
        {
            "@position_x" => "@position.x",
            "@position_y" => "@position.y",
            "@effect_id" => "@effect",
            "@handitem_id" => "@handitem",
            "@team_score" => "@team.score",
            "@team_color" => "@team.color",
            _ => key
        };
    }
}
