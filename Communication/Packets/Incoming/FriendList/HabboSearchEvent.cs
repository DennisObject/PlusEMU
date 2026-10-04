using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class HabboSearchEvent : IPacketEvent
{
    private readonly ISearchResultFactory _searchResultFactory;
    private readonly IGameClientManager _clients;

    public HabboSearchEvent(ISearchResultFactory searchResultFactory, IGameClientManager clients)
    {
        _searchResultFactory = searchResultFactory;
        _clients = clients;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var query = StringCharFilter.Escape(packet.ReadString().Replace("%", ""));
        if (query.Length < 1 || query.Length > 100)
            return Task.CompletedTask;
        var friends = new List<HabboSearchEntry>();
        var othersUsers = new List<HabboSearchEntry>();
        var habbo = session.GetHabbo();
        var results = _searchResultFactory.GetSearchResult(query);
        foreach (var result in results.ToList())
        {
            if (habbo.Messenger.FriendshipExists(result.UserId))
                friends.Add(new(result, _clients.GetClientByUserId(result.UserId) != null));
            else
                othersUsers.Add(new(result, _clients.GetClientByUserId(result.UserId) != null));
        }
        session.Send(new HabboSearchResultComposer(friends, othersUsers));
        return Task.CompletedTask;
    }
}
