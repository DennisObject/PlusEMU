using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms;

public interface IRoomMuteService
{
    void Mute(GameClient session, int userId, int durationMinutes);
}

public sealed class RoomMuteService(IAchievementManager achievements, TimeProvider clock) : IRoomMuteService
{
    public void Mute(GameClient session, int userId, int durationMinutes)
    {
        var habbo = session.GetHabbo();

        if (!habbo.InRoom || habbo.CurrentRoom is not { } room)
        {
            return;
        }

        if (room.WhoCanMute == 0 && !room.CheckRights(session, true) && room.Group == null
            || room.WhoCanMute == 1 && !room.CheckRights(session) && room.Group == null
            || room.Group != null && !room.CheckRights(session, false, true))
        {
            return;
        }

        var target = room.GetRoomUserManager().GetRoomUserByHabbo(userId);
        var targetClient = target?.GetClient();
        var targetHabbo = targetClient?.GetHabbo();

        if (target == null || targetClient == null || targetHabbo == null
            || !RoomModerationPolicy.CanTarget(habbo.Access, targetHabbo.Access))
        {
            return;
        }

        var now = clock.GetUtcNow();

        if (!RoomMuteDeadline.TryCreate(now, durationMinutes, out var mutedUntil))
        {
            return;
        }

        if (room.MutedUsers.TryGetValue(userId, out var currentUntil) && now < currentUntil)
        {
            return;
        }

        room.MutedUsers[userId] = mutedUntil;
        targetClient.SendWhisper($"The room owner has muted you for {durationMinutes} minutes!");
        achievements.ProgressAchievement(session, "ACH_SelfModMuteSeen", 1);
    }
}

internal static class RoomMuteDeadline
{
    internal static bool TryCreate(DateTimeOffset now, int durationMinutes, out DateTimeOffset mutedUntil)
    {
        mutedUntil = default;

        if (durationMinutes <= 0 || durationMinutes > int.MaxValue / 60
            || durationMinutes > (DateTimeOffset.MaxValue - now).TotalMinutes)
        {
            return false;
        }

        mutedUntil = now.AddMinutes(durationMinutes);

        return true;
    }
}
