using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class BanUserEvent : IPacketEvent
{
    private readonly IAchievementManager _achievementManager;

    public BanUserEvent(IAchievementManager achievementManager)
    {
        _achievementManager = achievementManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        if (room.WhoCanBan == 0 && !room.CheckRights(session, true) && room.Group == null || room.WhoCanBan == 1 && !room.CheckRights(session) && room.Group == null ||
            room.Group != null && !room.CheckRights(session, false, true))
            return Task.CompletedTask;
        var userId = packet.ReadInt();
        packet.ReadInt(); //roomId
        var r = packet.ReadString();
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(Convert.ToInt32(userId));
        if (user == null || user.IsBot)
            return Task.CompletedTask;
        if (room.OwnerId == userId)
            return Task.CompletedTask;
        if (!RoomModerationPolicy.CanTarget(session.GetHabbo().Access, user.GetClient().GetHabbo().Access))
            return Task.CompletedTask;
        var duration = TimeSpan.Zero;
        if (r.ToLower().Contains("hour"))
            duration = TimeSpan.FromHours(1);
        else if (r.ToLower().Contains("day"))
            duration = TimeSpan.FromDays(1);
        else if (r.ToLower().Contains("perm"))
            duration = TimeSpan.FromSeconds(78892200);
        room.GetBans().Ban(user, duration);
        _achievementManager.ProgressAchievement(session, "ACH_SelfModBanSeen", 1);
        return Task.CompletedTask;
    }
}
