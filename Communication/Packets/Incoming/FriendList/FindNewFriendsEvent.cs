using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class FindNewFriendsEvent(INavigatorPresentationService navigator) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        navigator.FindFriends(session);
        return Task.CompletedTask;
    }
}