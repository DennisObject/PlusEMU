using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public class FlatControllerAddedComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly int _userId;
    private readonly string _username;
    public uint MessageId => ServerPacketHeader.FlatControllerAddedComposer;

    public FlatControllerAddedComposer(uint roomId, int userId, string username)
    {
        _roomId = roomId;
        _userId = userId;
        _username = username;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteInteger(_userId);
        packet.WriteString(_username);
    }
}
