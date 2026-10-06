using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class RoomInviteComposer : IServerPacket
{
    private readonly int _senderId;
    private readonly string _text;

    public uint MessageId => ServerPacketHeader.RoomInviteComposer;

    public RoomInviteComposer(int senderId, string text)
    {
        _senderId = senderId;
        _text = text;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_senderId);
        packet.WriteString(_text);
    }
}
