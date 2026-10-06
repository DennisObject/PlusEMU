using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;
using Plus.Utilities;

namespace Plus.HabboHotel.Friends;

public interface IMessengerNavigationService
{
    void Search(GameClient session, string query);
    void Follow(GameClient session, int buddyId);
}

public sealed class MessengerNavigationService(ISearchResultFactory search, IGameClientManager clients) : IMessengerNavigationService
{
    public void Search(GameClient session, string query)
    {
        query = StringCharFilter.Escape(query.Replace("%", ""));

        if (query.Length < 1 || query.Length > 100)
        {
            return;
        }

        var habbo = session.GetHabbo();
        var friends = new List<HabboSearchEntry>();
        var others = new List<HabboSearchEntry>();

        foreach (var result in search.GetSearchResult(query).ToList())
        {
            var entry = new HabboSearchEntry(result, clients.GetClientByUserId(result.UserId) != null);

            if (habbo.Messenger.FriendshipExists(result.UserId))
            {
                friends.Add(entry);
            }
            else
            {
                others.Add(entry);
            }
        }

        session.Send(new HabboSearchResultComposer(friends, others));
    }

    public void Follow(GameClient session, int buddyId)
    {
        var habbo = session.GetHabbo();

        if (buddyId == 0 || buddyId == habbo.Id)
        {
            return;
        }

        var target = clients.GetClientByUserId(buddyId)?.GetHabbo();

        if (target == null)
        {
            return;
        }

        var room = target.CurrentRoom;

        if (room == null)
        {
            session.Send(new FollowFriendFailedComposer(FriendFollowError.Unavailable));

            return;
        }

        if (habbo.CurrentRoom?.RoomId == room.RoomId)
        {
            return;
        }

        habbo.PendingFollowRoomId = room.RoomId;
        session.Send(new RoomForwardComposer(room.RoomId));
    }
}
