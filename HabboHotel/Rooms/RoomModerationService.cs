using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms;

public sealed record RoomBanRequest(int UserId, int RoomId, string Duration);

public interface IRoomModerationService
{
    void Kick(GameClient session, int userId);
    void Ban(GameClient session, RoomBanRequest request);
    void Unban(GameClient session, int userId, int packetRoomId);
    void ToggleMute(GameClient session);
    void AnswerDoor(Room room, GameClient session, string username, bool accepted);
}

public sealed class RoomModerationService(IAchievementManager achievements, IGameClientManager clients) : IRoomModerationService
{
    public void Kick(GameClient session, int userId)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;

        if (room == null || !room.CheckRights(session) && room.WhoCanKick != 2 && room.Group == null
            || room.Group != null && !room.CheckRights(session, false, true))
        {
            return;
        }

        if (!TryGetTarget(room, habbo.Access, userId, out _, out var target))
        {
            return;
        }

        room.GetRoomUserManager().RemoveUserFromRoom(target, true, true);
        achievements.ProgressAchievement(session, "ACH_SelfModKickSeen", 1);
    }

    public void Ban(GameClient session, RoomBanRequest request)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;

        if (room == null || room.WhoCanBan == 0 && !room.CheckRights(session, true) && room.Group == null
            || room.WhoCanBan == 1 && !room.CheckRights(session) && room.Group == null
            || room.Group != null && !room.CheckRights(session, false, true))
        {
            return;
        }

        if (!TryGetTarget(room, habbo.Access, request.UserId, out var user, out _))
        {
            return;
        }

        // The packet room id was historically ignored; the actor's current room remains authoritative.
        var duration = TimeSpan.Zero;
        var value = request.Duration.ToLower();

        if (value.Contains("hour"))
        {
            duration = TimeSpan.FromHours(1);
        }
        else if (value.Contains("day"))
        {
            duration = TimeSpan.FromDays(1);
        }
        else if (value.Contains("perm"))
        {
            duration = TimeSpan.FromSeconds(78892200);
        }

        room.GetBans().Ban(user, duration);
        achievements.ProgressAchievement(session, "ACH_SelfModBanSeen", 1);
    }

    public void Unban(GameClient session, int userId, int packetRoomId)
    {
        var room = session.GetHabbo().CurrentRoom;

        if (room == null || !room.CheckRights(session, true))
        {
            return;
        }

        if (room.GetBans().IsBanned(userId) && room.GetBans().Unban(userId))
        {
            session.Send(new UnbanUserFromRoomComposer(packetRoomId, userId));
        }
    }

    public void ToggleMute(GameClient session)
    {
        var room = session.GetHabbo().CurrentRoom;

        if (room == null || !room.CheckRights(session, true))
        {
            return;
        }

        room.RoomMuted = !room.RoomMuted;

        foreach (var user in room.GetRoomUserManager().GetRoomUsers().ToArray())
        {
            var client = user?.GetClient();

            if (client != null)
            {
                client.SendWhisper(room.RoomMuted ? "This room has been muted" : "This room has been unmuted");
            }
        }

        room.SendPacket(new RoomMuteSettingsComposer(room.RoomMuted));
    }

    public void AnswerDoor(Room room, GameClient session, string username, bool accepted)
    {
        if (!ReferenceEquals(session.GetHabbo().CurrentRoom, room) || !room.CheckRights(session))
        {
            return;
        }

        var targetClient = clients.GetClientByUsername(username);
        var target = targetClient?.GetHabbo();

        if (targetClient == null || target == null)
        {
            return;
        }

        var name = target.Username;

        if (accepted)
        {
            target.RoomAuthOk = true;
            targetClient.Send(new FlatAccessibleComposer(""));
            room.SendPacket(new FlatAccessibleComposer(name), true);
        }
        else
        {
            targetClient.Send(new FlatAccessDeniedComposer(""));
            room.SendPacket(new FlatAccessDeniedComposer(name), true);
        }
    }

    private bool TryGetTarget(Room room, UserAccess actor, int userId, out RoomUser user, out GameClient target)
    {
        user = room.GetRoomUserManager().GetRoomUserByHabbo(userId)!;
        target = null!;

        if (user == null || user.IsBot || room.OwnerId == userId)
        {
            return false;
        }

        var client = clients.GetClientByUserId(userId);
        var habbo = client?.GetHabbo();

        if (client == null || habbo == null || !ReferenceEquals(user.GetClient(), client)
            || !ReferenceEquals(habbo.CurrentRoom, room)
            || !RoomModerationPolicy.CanTarget(actor, habbo.Access))
        {
            return false;
        }

        target = client;

        return true;
    }
}
