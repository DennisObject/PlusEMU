using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Session;

public class CantConnectComposer : IServerPacket
{
    private readonly RoomConnectionError _error;
    public uint MessageId => ServerPacketHeader.CantConnectComposer;
    public CantConnectComposer(RoomConnectionError error)
    {
        _error = error;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger((int)_error);
}
