using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class AcceptFriendEvent(IMessengerFriendMutationService friends) : IPacketEvent
{
    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var amount = packet.ReadInt();

        if (amount > 50)
        {
            amount = 50;
        }
        else if (amount < 0)
        {
            return;
        }

        var requestIds = new int[amount];

        for (var i = 0; i < amount; i++)
        {
            requestIds[i] = packet.ReadInt();
        }

        foreach (var requestId in requestIds)
        {
            await friends.AcceptRequestAsync(session.GetHabbo(), requestId);
        }
    }
}
