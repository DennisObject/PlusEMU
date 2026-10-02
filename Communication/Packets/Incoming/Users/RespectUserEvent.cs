using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Users;

internal class RespectUserEvent : RoomPacketEvent
{
    private readonly IAchievementManager _achievementManager;
    private readonly IQuestManager _questManager;

    public RespectUserEvent(IAchievementManager achievementManager, IQuestManager questManager)
    {
        _achievementManager = achievementManager;
        _questManager = questManager;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (session.GetHabbo().HabboStats.DailyRespectPoints <= 0)
            return Task.CompletedTask;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(packet.ReadInt());
        if (user == null || user.GetClient() == null || user.GetClient().GetHabbo().Id == session.GetHabbo().Id || user.IsBot)
            return Task.CompletedTask;
        var thisUser = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (thisUser == null)
            return Task.CompletedTask;
        _questManager.ProgressUserQuest(session, QuestType.SocialRespect);
        _achievementManager.ProgressAchievement(session, "ACH_RespectGiven", 1);
        _achievementManager.ProgressAchievement(user.GetClient(), "ACH_RespectEarned", 1);
        session.GetHabbo().HabboStats.DailyRespectPoints -= 1;
        RewardTrackManager.Current?.Progress(session, RewardTrackActions.GiveRespect);
        session.GetHabbo().HabboStats.RespectGiven += 1;
        user.GetClient().GetHabbo().HabboStats.Respect += 1;
        if (room.RespectNotificationsEnabled)
            room.SendPacket(new RespectNotificationComposer(user.GetClient().GetHabbo().Id, user.GetClient().GetHabbo().HabboStats.Respect));
        room.SendPacket(new ActionComposer(thisUser.VirtualId, 7));
        return Task.CompletedTask;
    }
}