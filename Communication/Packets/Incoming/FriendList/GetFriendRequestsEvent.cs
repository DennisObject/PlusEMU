using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;
using Plus.HabboHotel.Cache;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class GetFriendRequestsEvent(ICacheManager cache) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var requests = session.GetHabbo().Messenger.Requests.Values
            .Select(request => new FriendRequestData(request.FromId, request.Username,
                cache.GenerateUser(request.FromId)?.Look ?? string.Empty))
            .ToList();
        session.Send(new FriendRequestsComposer(requests));
        return Task.CompletedTask;
    }
}
