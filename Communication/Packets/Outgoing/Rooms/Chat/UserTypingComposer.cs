using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Chat;

public class UserTypingComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly bool _typing;

    public uint MessageId => ServerPacketHeader.UserTypingComposer;

    public UserTypingComposer(int virtualId, bool typing)
    {
        _virtualId = virtualId;
        _typing = typing;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_virtualId);
        packet.WriteInteger(_typing ? 1 : 0);
    }
}
