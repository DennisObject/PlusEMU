using System.Globalization;
using Dapper;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Core.Language;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Moderation;

public sealed record ModerateRoomRequest(uint RoomId, bool Lock, bool Rename, bool KickAll);

public interface IModeratorActionStore
{
    void AddCaution(int userId);
    void SetMute(int userId, long seconds);
    void ModerateRoom(uint roomId, bool rename, bool locked, bool endPromotion);
}

public sealed class ModeratorActionStore(IDatabase database) : IModeratorActionStore
{
    public void AddCaution(int userId)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE user_info SET cautions = cautions + 1 WHERE user_id = @userId LIMIT 1", new { userId });
    }

    public void SetMute(int userId, long seconds)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE users SET time_muted = @seconds WHERE id = @userId LIMIT 1", new { seconds, userId });
    }

    public void ModerateRoom(uint roomId, bool rename, bool locked, bool endPromotion)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            UPDATE rooms SET caption = CASE WHEN @rename THEN @caption ELSE caption END,
                description = CASE WHEN @rename THEN @caption ELSE description END,
                state = CASE WHEN @locked THEN 'locked' ELSE state END, tags = ''
            WHERE id = @roomId LIMIT 1
            """, new { roomId, rename, locked, caption = ModeratorActionService.InappropriateRoomText }, transaction);
        if (endPromotion) connection.Execute("DELETE FROM room_promotions WHERE room_id = @roomId", new { roomId }, transaction);
        transaction.Commit();
    }
}

public interface IModeratorActionService
{
    void Caution(GameClient actor, int targetId, string message);
    void Mute(GameClient actor, int targetId, int minutes);
    void Kick(GameClient actor, int targetId);
    void ModerateRoom(GameClient actor, ModerateRoomRequest request);
}

public sealed class ModeratorActionService(IGameClientManager clients, IModeratorUserLookup users, IRoomManager rooms,
    IAccessControl access, ILanguageManager language, IModeratorActionStore store) : IModeratorActionService
{
    internal const string InappropriateRoomText = "Inappropriate to Hotel Management";

    public void Caution(GameClient actor, int targetId, string message)
    {
        var target = clients.GetClientByUserId(targetId);
        var habbo = target?.GetHabbo();
        if (habbo == null || !actor.GetHabbo().Access.Outranks(habbo.Access)) return;
        store.AddCaution(habbo.Id);
        target!.SendNotification(message);
    }

    public void Mute(GameClient actor, int targetId, int minutes)
    {
        if (minutes < 0) return;
        var target = users.GetById(targetId);
        if (target == null)
        {
            actor.SendWhisper("An error occoured whilst finding that user in the database.");
            return;
        }
        if (!actor.GetHabbo().Access.Outranks(target.Access))
        {
            actor.SendWhisper("Oops, you cannot mute that user.");
            return;
        }
        var seconds = (long)minutes * 60;
        store.SetMute(target.Id, seconds);
        target.TimeMuted = seconds;
        target.Client?.SendNotification($"You have been muted by a moderator for {seconds.ToString(CultureInfo.InvariantCulture)} seconds!");
    }

    public void Kick(GameClient actor, int targetId)
    {
        var target = clients.GetClientByUserId(targetId);
        var habbo = target?.GetHabbo();
        var room = habbo?.CurrentRoom;
        if (habbo == null || room == null || habbo.Id == actor.GetHabbo().Id) return;
        if (!actor.GetHabbo().Access.Outranks(habbo.Access))
        {
            actor.SendNotification(language.TryGetValue("moderation.kick.disallowed"));
            return;
        }
        room.GetRoomUserManager().RemoveUserFromRoom(target!, true);
    }

    public void ModerateRoom(GameClient actor, ModerateRoomRequest request)
    {
        if (!rooms.TryGetRoom(request.RoomId, out var room)) return;
        var habbo = actor.GetHabbo();
        if (room.OwnerId != habbo.Id && !access.Outranks(habbo.Id, room.OwnerId)) return;
        store.ModerateRoom(room.Id, request.Rename, request.Lock, room.HasActivePromotion);
        if (request.Rename) { room.Name = InappropriateRoomText; room.Description = InappropriateRoomText; }
        if (request.Lock) room.Access = RoomAccess.Doorbell;
        room.ClearTags();
        room.EndPromotion();
        room.SendPacket(new RoomSettingsSavedComposer(room.Id));
        room.SendPacket(new RoomInfoUpdatedComposer(room.Id));
        if (!request.KickAll) return;
        foreach (var user in room.GetRoomUserManager().GetUserList().ToList())
        {
            if (user == null || user.IsBot) continue;
            var client = user.GetClient();
            var target = client?.GetHabbo();
            if (target == null || target.Id == habbo.Id || !habbo.Access.Outranks(target.Access)) continue;
            room.GetRoomUserManager().RemoveUserFromRoom(client!, true);
        }
    }
}
