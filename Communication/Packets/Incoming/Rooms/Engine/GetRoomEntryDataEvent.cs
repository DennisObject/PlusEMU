using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class GetRoomEntryDataEvent : IPacketEvent
{
    private readonly IQuestManager _questManager;
    private readonly TimeProvider _clock;

    public GetRoomEntryDataEvent(IQuestManager questManager, TimeProvider clock)
    {
        _questManager = questManager;
        _clock = clock;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        if (!room.GetRoomUserManager().AddAvatarToRoom(session))
        {
            room.GetRoomUserManager().RemoveUserFromRoom(session, false);
            return Task.CompletedTask; //TODO: Remove?
        }
        room.SendObjects(session);
        if (session.GetHabbo().Messenger != null)
            session.GetHabbo().Messenger.NotifyChangesToFriends();
        if (session.GetHabbo().HabboStats.QuestId > 0)
            _questManager.QuestReminder(session, session.GetHabbo().HabboStats.QuestId);
        session.Send(new RoomEntryInfoComposer(room.RoomId, room.CheckRights(session, true)));
        session.Send(new RoomVisualizationSettingsComposer(room.WallThickness, room.FloorThickness, Convert.ToBoolean(room.Hidewall)));
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Username);
        if (user != null && session.GetHabbo().PetId == 0) room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
        session.Send(new RoomEventComposer(RoomEventSnapshot.Capture(room.Data, room.Promotion)));
        if (room.GetWired() != null)
            room.GetWired().TriggerEvent(WiredBoxType.TriggerRoomEnter, session.GetHabbo());
        var now = _clock.GetUtcNow();
        if (session.GetHabbo().FloodUntil is { } floodUntil && now < floodUntil)
            session.Send(new FloodControlComposer(RemainingFloodSeconds(now, floodUntil)));
        return Task.CompletedTask;
    }

    internal static int RemainingFloodSeconds(DateTimeOffset now, DateTimeOffset floodUntil) =>
        (int)Math.Clamp((long)Math.Ceiling((floodUntil - now).TotalSeconds), 0, int.MaxValue);
}
