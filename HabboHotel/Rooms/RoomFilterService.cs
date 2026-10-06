using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms;

public interface IRoomFilterService
{
    void Show(GameClient session);
    void Modify(GameClient session, int roomId, bool added, string word);
}

public sealed class RoomFilterService(IAchievementManager achievements) : IRoomFilterService
{
    public void Show(GameClient session)
    {
        if (!TryGetAuthorizedRoom(session, out var room))
        {
            return;
        }

        session.Send(new GetRoomFilterListComposer(room.WordFilterList));
        achievements.ProgressAchievement(session, "ACH_SelfModRoomFilterSeen", 1);
    }

    public void Modify(GameClient session, int roomId, bool added, string word)
    {
        if (!TryGetAuthorizedRoom(session, out var room) || roomId < 0 || room.Id != (uint)roomId)
        {
            return;
        }

        if (added)
        {
            room.GetFilter().AddFilter(word);
        }
        else
        {
            room.GetFilter().RemoveFilter(word);
        }
    }

    private static bool TryGetAuthorizedRoom(GameClient session, out Room room)
    {
        room = session.GetHabbo().CurrentRoom!;

        return room != null && room.CheckRights(session);
    }
}
