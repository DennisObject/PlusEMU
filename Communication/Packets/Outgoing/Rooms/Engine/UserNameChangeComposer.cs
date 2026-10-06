using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class UserNameChangeComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly int _virtualId;
    private readonly string _username;

    public uint MessageId => ServerPacketHeader.UserNameChangeComposer;

    public UserNameChangeComposer(uint roomId, int virtualId, string username)
    {
        _roomId = roomId;
        _virtualId = virtualId;
        _username = username;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteInteger(_virtualId);
        packet.WriteString(_username);
    }
}
