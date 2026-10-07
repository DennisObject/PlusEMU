using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.HabboHotel.Rooms;

public interface IRoomEntryService
{
    void Enter(GameClient session);
}

public sealed class RoomEntryService(IQuestManager quests, TimeProvider clock, IRoomPollService polls, IRoomWordQuizService quizzes) : IRoomEntryService
{
    public void Enter(GameClient session)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;

        if (room == null) {
            return;
        }

        if (!room.GetRoomUserManager().AddAvatarToRoom(session)) {
            room.GetRoomUserManager().RemoveUserFromRoom(session, false);

            return;
        }

        room.SendObjects(session);
        habbo.Messenger?.NotifyChangesToFriends();

        if (habbo.HabboStats.QuestId > 0) {
            quests.QuestReminder(session, habbo.HabboStats.QuestId);
        }

        session.Send(new RoomEntryInfoComposer(room.RoomId, room.CheckRights(session, true)));
        session.Send(new RoomVisualizationSettingsComposer(room.WallThickness, room.FloorThickness, Convert.ToBoolean(room.Hidewall)));
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Username);

        if (user != null && habbo.PetId == 0) {
            room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
        }

        session.Send(new RoomEventComposer(RoomEventSnapshot.Capture(room.Data, room.Promotion)));
        room.GetWired()?.TriggerEvent(WiredBoxType.TriggerRoomEnter, habbo);
        var now = clock.GetUtcNow();

        if (habbo.FloodUntil is { } floodUntil && now < floodUntil) {
            session.Send(new FloodControlComposer(RemainingFloodSeconds(now, floodUntil)));
        }

        polls.Offer(session);
        quizzes.Show(session);
    }

    internal static int RemainingFloodSeconds(DateTimeOffset now, DateTimeOffset floodUntil) =>
        (int)Math.Clamp((long)Math.Ceiling((floodUntil - now).TotalSeconds), 0, int.MaxValue);
}
