using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;

namespace Plus.HabboHotel.Rooms;

public interface IRoomRespectService
{
    void Respect(Room room, GameClient session, int userId);
}

public sealed class RoomRespectService(IAchievementManager achievements, IQuestManager quests,
    IRewardTrackManager rewards) : IRoomRespectService
{
    public void Respect(Room room, GameClient session, int userId)
    {
        var habbo = session.GetHabbo();
        var stats = habbo.HabboStats;

        if (!ReferenceEquals(habbo.CurrentRoom, room) || stats == null || stats.DailyRespectPoints <= 0) {
            return;
        }

        var users = room.GetRoomUserManager();
        var user = users.GetRoomUserByHabbo(userId);

        if (user == null || user.IsBot) {
            return;
        }

        var targetClient = user.GetClient();
        var target = targetClient?.GetHabbo();

        if (target == null || target.Id == habbo.Id || target.HabboStats == null
            || !ReferenceEquals(target.CurrentRoom, room)) {
            return;
        }

        var actor = users.GetRoomUserByHabbo(habbo.Id);

        if (actor == null) {
            return;
        }

        quests.ProgressUserQuest(session, QuestType.SocialRespect);
        achievements.ProgressAchievement(session, "ACH_RespectGiven", 1);
        achievements.ProgressAchievement(targetClient!, "ACH_RespectEarned", 1);
        stats.DailyRespectPoints--;
        rewards.Progress(session, RewardTrackActions.GiveRespect);
        stats.RespectGiven++;
        target.HabboStats.Respect++;

        if (room.RespectNotificationsEnabled) {
            room.SendPacket(new RespectNotificationComposer(target.Id, target.HabboStats.Respect));
        }

        room.SendPacket(new ActionComposer(actor.VirtualId, 7));
        room.GetWired().Dispatch(new(WiredEventKind.AvatarAction) { Actor = actor, Action = (int)WiredAvatarAction.Respect });
    }
}
