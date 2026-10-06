using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class CanCreateRoomComposer : IServerPacket
{
    private readonly bool _error;
    private readonly int _maxRoomsPerUser;
    public uint MessageId => ServerPacketHeader.CanCreateRoomComposer;

    public CanCreateRoomComposer(bool error, int maxRoomsPerUser)
    {
        _error = error;
        _maxRoomsPerUser = maxRoomsPerUser;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_error ? 1 : 0);
        packet.WriteInteger(_maxRoomsPerUser);
    }
}
