using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class MessengerErrorComposer : IServerPacket
{
    private readonly int _errorCode1;
    private readonly int _errorCode2;

    public uint MessageId => ServerPacketHeader.MessengerErrorComposer;

    public MessengerErrorComposer(int errorCode1, int errorCode2)
    {
        _errorCode1 = errorCode1;
        _errorCode2 = errorCode2;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_errorCode1);
        packet.WriteInteger(_errorCode2);
    }
}
