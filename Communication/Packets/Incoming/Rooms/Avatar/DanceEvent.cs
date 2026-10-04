using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

internal class DanceEvent : RoomPacketEvent
{
    private readonly IQuestManager _questManager;

    public DanceEvent(IQuestManager questManager)
    {
        _questManager = questManager;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null)
            return Task.CompletedTask;
        user.UnIdle();
        var danceId = packet.ReadInt();
        if (danceId < 0 || danceId > 4 || danceId > 1 && Plus.HabboHotel.Subscriptions.ClubAccess.LevelFor(session.GetHabbo().Access) == 0)
            danceId = 0;
        if (danceId > 0 && user.CarryItemId > 0)
            user.CarryItem(0);
        if (session.GetHabbo().Effects.CurrentEffect > 0)
            room.SendPacket(new AvatarEffectComposer(user.VirtualId, 0));
        var previousDance = user.DanceId;
        user.DanceId = danceId;
        room.SendPacket(new DanceComposer(user, danceId));
        if (danceId > 0)
            room.GetWired().Dispatch(new(WiredEventKind.AvatarAction) { Actor = user, Action = (int)WiredAvatarAction.Dance, Code = danceId });
        if (danceId >= 1 && danceId <= 4 && danceId != previousDance)
            RewardTrackManager.Current?.Progress(session, RewardTrackActions.Dance);
        _questManager.ProgressUserQuest(session, QuestType.SocialDance);
        if (room.GetRoomUserManager().GetRoomUsers().Count > 19)
            _questManager.ProgressUserQuest(session, QuestType.MassDance);
        return Task.CompletedTask;
    }
}