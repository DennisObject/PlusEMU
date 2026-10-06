using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class BuddyListComposer(ImmutableArray<MessengerBuddySnapshot> friends, int pages, int page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.BuddyListComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(pages); // Pages
        packet.WriteInteger(page); // Page
        packet.WriteInteger(friends.Length);

        foreach (var friend in friends)
        {
            MessengerBuddyWire.Write(packet, friend);
        }
    }
}
