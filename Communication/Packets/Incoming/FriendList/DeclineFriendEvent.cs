using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class DeclineFriendEvent(IMessengerFriendMutationService friends) : IPacketEvent
{
    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var declineAll = packet.ReadBool();
        packet.ReadInt(); //amount
        var requestId = declineAll ? 0 : packet.ReadInt();

        if (declineAll)
        {
            await friends.DeclineAllRequestsAsync(session.GetHabbo());
        }
        else
        {
            await friends.DeclineRequestAsync(session.GetHabbo(), requestId);
        }
    }
}
