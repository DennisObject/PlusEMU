using Plus.Communication.Packets.Outgoing.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.HabboHotel.Rooms.AI;

public interface IPetRespectService
{
    void Respect(Room room, GameClient session, int petId);
}

public sealed class PetRespectService(IAchievementManager achievements, IQuestManager quests,
    IRewardTrackManager rewards) : IPetRespectService
{
    public void Respect(Room room, GameClient session, int petId)
    {
        var habbo = session.GetHabbo();
        var stats = habbo.HabboStats;
        if (!ReferenceEquals(habbo.CurrentRoom, room) || stats == null || stats.DailyPetRespectPoints <= 0)
            return;
        var users = room.GetRoomUserManager();
        var actor = users.GetRoomUserByHabbo(habbo.Id);
        if (actor == null)
            return;
        if (!users.TryGetPet(petId, out var pet))
        {
            var targetUser = users.GetRoomUserByHabbo(petId);
            var targetClient = targetUser?.GetClient();
            var target = targetClient?.GetHabbo();
            if (targetUser == null || target == null || target.HabboStats == null
                || !ReferenceEquals(target.CurrentRoom, room))
                return;
            if (target.Id == habbo.Id)
            {
                session.SendWhisper("Oops, you cannot use this on yourself! (You haven't lost a point, simply reload!)");
                return;
            }
            quests.ProgressUserQuest(session, QuestType.SocialRespect);
            achievements.ProgressAchievement(session, "ACH_RespectGiven", 1);
            achievements.ProgressAchievement(targetClient!, "ACH_RespectEarned", 1);
            stats.DailyPetRespectPoints--;
            stats.RespectGiven++;
            target.HabboStats.Respect++;
            actor.CarryItemId = 999999999;
            actor.CarryTimer = 5;
            if (room.RespectNotificationsEnabled)
                room.SendPacket(new RespectPetNotificationComposer(targetUser.VirtualId, target.Id, target.Username, "FFFFFF"));
            room.SendPacket(new CarryObjectComposer(actor.VirtualId, actor.CarryItemId));
            return;
        }
        if (pet?.PetData == null || pet.RoomId != room.Id)
            return;
        stats.DailyPetRespectPoints--;
        achievements.ProgressAchievement(session, "ACH_PetRespectGiver", 1);
        actor.CarryItemId = 999999999;
        actor.CarryTimer = 5;
        pet.PetData.OnRespect();
        rewards.Progress(session, RewardTrackActions.PetRespect);
        room.SendPacket(new CarryObjectComposer(actor.VirtualId, actor.CarryItemId));
    }
}
