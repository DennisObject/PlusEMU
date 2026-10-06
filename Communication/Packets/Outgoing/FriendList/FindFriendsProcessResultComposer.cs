using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FindFriendsProcessResultComposer : IServerPacket
{
    private readonly bool _found;
    public uint MessageId => ServerPacketHeader.FindFriendsProcessResultComposer;

    public FindFriendsProcessResultComposer(bool found)
    {
        _found = found;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_found);
    }
}
