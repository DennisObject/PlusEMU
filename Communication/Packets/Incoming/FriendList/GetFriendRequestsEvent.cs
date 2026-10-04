using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class GetFriendRequestsEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        ICollection<MessengerRequest> requests = session.GetHabbo().Messenger.Requests.Values.ToList();
        session.Send(new FriendRequestsComposer(requests));
        return Task.CompletedTask;
    }
}