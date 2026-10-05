using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class RemoveFriendEvent(IMessengerFriendMutationService friends) : IPacketEvent
{
    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var amount = packet.ReadInt();
        if (amount > 100)
            amount = 100;
        else if (amount < 0)
            return;

        var friendIds = new int[amount];
        for (var i = 0; i < amount; i++)
            friendIds[i] = packet.ReadInt();

        await friends.RemoveFriendsAsync(session.GetHabbo(), friendIds);
    }
}