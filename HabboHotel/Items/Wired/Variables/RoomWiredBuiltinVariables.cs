using System.Globalization;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>
/// Reads live room state. Missing engine-owned properties (projectile, sign, entry provenance, visibility)
/// are absent unless the engine supplies them; they are never fabricated as zero. Movement is supplied
/// by the stack engine so its collision, animation and carried-user policy applies to variable writes too.
/// </summary>
public sealed class RoomWiredBuiltinVariables(Room room,
    Func<WiredVariableReference, WiredVariableHolder, WiredVariableFrame, int?>? engineRead = null,
    Func<WiredVariableReference, WiredVariableHolder, int, WiredVariableFrame, bool>? engineWrite = null) : IWiredBuiltinVariables
{
    public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        if (reference.Target != holder.Target || frame.RoomId != room.Id || !frame.Contains(holder)) return null;
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
        return value is int number ? new(number, 0, 0) : null;
    }

    public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame)
    {
        if (reference.Target != holder.Target || frame.RoomId != room.Id || !frame.Contains(holder)) return false;
        var key = Normalize(reference.Token);
        if (holder.Target == WiredVariableTarget.User)
        {
            var avatar = room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);
            if (avatar is null || !Matches(avatar, holder)) return false;
            if (key == "@handitem" && value is >= 0 and <= 9999)
            {
                avatar.CarryItem(value);
                return true;
            }
            if (key == "@team.score" && !avatar.IsBot && avatar.Team != Team.None && value >= 0)
            {
                var game = room.GetGameManager();
                var current = game.Points[(int)avatar.Team];
                var difference = (long)value - current;
                if (difference is < int.MinValue or > int.MaxValue) return false;
                game.AddPointToTeam(avatar.Team, (int)difference);
                return true;
            }
        }
        if (holder.Target == WiredVariableTarget.Furni && key == "@state")
        {
            var item = FindItem(holder);
            if (item is null || value < 0 || item.Definition.Modes <= value) return false;
            item.LegacyDataString = value.ToString(CultureInfo.InvariantCulture);
            item.UpdateState();
            return true;
        }
        return engineWrite?.Invoke(reference with { Token = $"internal:{key}" }, holder, value, frame) == true;
    }

    private Item? FindItem(WiredVariableHolder holder) => holder.StableId is > 0 and <= uint.MaxValue
        && holder.StableId == holder.EntityId ? room.GetRoomItemHandler().GetItem((uint)holder.StableId) : null;
    private int? ReadItem(string key, WiredVariableHolder holder)
    {
        var item = FindItem(holder);
        if (item is null) return null;
        return key switch
        {
            "@id" => checked((int)item.Id), "@owner_id" => checked((int)item.OwnerId),
            "@class_id" => item.Definition.SpriteId, "@height" => Hundredths(item.TotalHeight - item.GetZ),
            "@state" => int.TryParse(item.LegacyDataString, out var state) ? state : null,
            "@position.x" => item.GetX, "@position.y" => item.GetY, "@altitude" => Hundredths(item.GetZ),
            "@rotation" => item.Rotation, "@dimensions.x" => item.Definition.Width, "@dimensions.y" => item.Definition.Length,
            "@is_stackable" => Flag(item.IsFloorItem && item.Definition.Stackable),
            "@can_stand_on" => Flag(item.IsFloorItem && item.Definition.Walkable),
            "@can_sit_on" => Flag(item.IsFloorItem && item.Definition.IsSeat),
            "@can_lay_on" => Flag(item.IsFloorItem && item.Definition.InteractionType == InteractionType.Bed),
            // Plus's persisted room items have ordinary ownership; temporary items must come from the engine adapter.
            "@type" => item.Id > 0 && item.OwnerId > 0 ? 0 : null,
            _ => null
        };
    }
    private int? ReadAvatar(string key, WiredVariableHolder holder)
    {
        var avatar = room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);
        if (avatar is null || !Matches(avatar, holder)) return null;
        var habbo = avatar.IsBot ? null : avatar.GetClient()?.GetHabbo();
        return key switch
        {
            "@index" => avatar.VirtualId, "@type" => avatar.IsPet ? 2 : avatar.IsBot ? 4 : 1,
            "@user_id" => habbo?.Id, "@bot_id" => avatar.IsBot && !avatar.IsPet ? avatar.BotData.Id : null,
            "@pet_id" => avatar.IsPet ? avatar.PetData.PetId : null,
            "@position.x" => avatar.X, "@position.y" => avatar.Y, "@altitude" => Hundredths(avatar.Z),
            "@direction" => avatar.RotBody, "@dance" => avatar.DanceId, "@handitem" => avatar.CarryItemId,
            "@effect" => habbo?.Effects.CurrentEffect, "@gender" => habbo is null ? -1 : habbo.Gender.Equals("M", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
            "@achievement_score" => habbo?.HabboStats.AchievementPoints,
            "@is_hc" => Flag(habbo is not null && habbo.VipRank > 0),
            "@is_idle" => Flag(avatar.IsAsleep), "@is_frozen" => Flag(avatar.Freezed || avatar.Frozen),
            "@is_trading" => Flag(avatar.IsTrading), "@is_muted" => Flag(habbo is not null && habbo.TimeMuted > 0),
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
            "@position_x" => "@position.x", "@position_y" => "@position.y", "@effect_id" => "@effect",
            "@handitem_id" => "@handitem", "@team_score" => "@team.score", "@team_color" => "@team.color", _ => key
        };
    }
}
