using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Session;

public class RoomForwardComposer : IServerPacket
{
    private readonly uint _roomId;
    public uint MessageId => ServerPacketHeader.RoomForwardComposer;

    public RoomForwardComposer(uint roomId)
    {
        _roomId = roomId;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteUInteger(_roomId);
}
