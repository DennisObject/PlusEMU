using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FollowFriendFailedComposer : IServerPacket
{
    private readonly FriendFollowError _errorCode;
    public uint MessageId => ServerPacketHeader.FollowFriendFailedComposer;

    public FollowFriendFailedComposer(FriendFollowError errorCode)
    {
        _errorCode = errorCode;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger((int)_errorCode);
}
